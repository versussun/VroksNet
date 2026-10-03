using System.IO.Compression;
using Mediator;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VroksNet.Application;
using VroksNet.Application.Abstractions;
using VroksNet.Application.Connections.DeleteConnection;
using VroksNet.Application.Provisioning;
using VroksNet.Application.Provisioning.ApplyProvisioning;
using VroksNet.Application.Provisioning.ExportProvisioning;
using VroksNet.Domain.TestScenarios;
using VroksNet.Infrastructure;
using VroksNet.Infrastructure.Persistence;

namespace VroksNet.UnitTests.Provisioning;

/// <summary>
/// Configuration export (ADR 0001, step A7) round-trips: provision one instance, export it, and
/// provision an empty one from the export — the same objects come back. Both instances are real:
/// temp-file SQLite, the real provisioning source, the real package writer.
/// </summary>
public sealed class ExportProvisioningTests : IAsyncLifetime
{
    private readonly List<Instance> _instances = [];
    private readonly string _sourceRoot = TempPath();

    private const string Manifest = """
        version: 1
        connections:
          - name: kafka
            type: Kafka
            valueFrom: ConnectionStrings:kafka
          - name: Bookstore HTTP
            type: Http
            value: http://bookstore:8080/v1?api-key=secret
          - name: rabbit
            type: RabbitMq
            value: amqp://guest:guest@rabbit:5672
        specifications:
          - title: Bookstore Sample API
            providerMode: true
            disabledOperations: ["DELETE /books/{bookId}"]
        publishers:
          - name: order-created
            specification: Shop Events Kafka Sample
            operation: "shop.orders.created:send"
            connection: kafka
            intervalSeconds: 60
            payloadOverride: "{\"note\": \"a \\\"quoted\\\" line\\nand another\", \"yes\": true}"
            enabled: false
          - name: order-created-rabbit
            specification: Shop Events Kafka Sample
            operation: "shop.orders.created:send"
            connection: rabbit
            intervalSeconds: 30
            brokerOptions: { exchange: shop }
        testScenarios:
          - name: list-books
            specification: Bookstore Sample API
            operation: "GET /books"
            connection: Bookstore HTTP
            schedule: { cron: "0 9 * * 1-5", timeZone: Europe/Kyiv }
          - name: settlements
            specification: Shop Events Kafka Sample
            operation: "shop.payments.{region}.settled:send"
            connection: kafka
            listenTimeoutSeconds: 120
          - name: settlements-rabbit
            specification: Shop Events Kafka Sample
            operation: "shop.payments.{region}.settled:send"
            connection: rabbit
            exchange: shop.events
        testSuites:
          - name: contract
            scenarios: [settlements, list-books]
            runOnStartup: true
        """;

    public ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(_sourceRoot, "specs"));
        foreach (var sample in new[] { "bookstore-openapi.yaml", "shop-events-kafka-asyncapi.yaml" })
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Samples", sample), Path.Combine(_sourceRoot, "specs", sample));
        }

        File.WriteAllText(Path.Combine(_sourceRoot, "vroksnet.yaml"), Manifest);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var instance in _instances)
        {
            await instance.DisposeAsync();
        }

        SqliteConnection.ClearAllPools();
        foreach (var instance in _instances)
        {
            File.Delete(instance.DbPath);
        }

        Directory.Delete(_sourceRoot, recursive: true);
    }

    [Fact]
    public async Task Export_ThenProvisionAnEmptyInstance_RecreatesTheConfiguration()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var source = await StartAsync(_sourceRoot, new() { ["ConnectionStrings:kafka"] = "broker:9092" });
        var original = await source.Send(new ApplyProvisioning());
        Assert.Equal(ProvisioningStatus.Applied, original.Status);

        var exported = Unzip(await source.Send(new ExportProvisioning()));
        var manifest = await File.ReadAllTextAsync(Path.Combine(exported, "vroksnet.yaml"), cancellationToken);
        Assert.DoesNotContain("api-key=secret", manifest); // values become valueFrom…
        Assert.Contains("#   ConnectionStrings__Bookstore_HTTP   (Http connection \"Bookstore HTTP\")", manifest); // …and the header says what to set

        // The variables the header names, as a deployment would set them.
        var copy = await StartAsync(exported, new()
        {
            ["ConnectionStrings:kafka"] = "broker:9092",
            ["ConnectionStrings:Bookstore_HTTP"] = "http://bookstore:8080/v1?api-key=secret",
            ["ConnectionStrings:rabbit"] = "amqp://guest:guest@rabbit:5672"
        });
        var report = await copy.Send(new ApplyProvisioning());

        Assert.True(report.Status == ProvisioningStatus.Applied, string.Join("\n", report.Errors.Select(e => $"{e.Source}: {e.Message}")) + "\n" + manifest);
        Assert.Equal(original.Counts, report.Counts);

        var bookstore = await copy.Get<IApiSpecificationRepository>().FindByTitleAsync("Bookstore Sample API", cancellationToken);
        Assert.False(bookstore!.Endpoints.Single(e => e.OperationKey == "DELETE /books/{bookId}").IsEnabled);
        Assert.True(bookstore.Endpoints.Single(e => e.OperationKey == "GET /books").ServeAtRealPath);

        Assert.Equal("http://bookstore:8080/v1?api-key=secret", (await copy.Get<IConnectionRepository>().FindByNameAsync("Bookstore HTTP", cancellationToken))!.Value);

        var originalPublisher = await source.Get<IPublisherRepository>().FindByNameAsync("order-created", cancellationToken);
        var publisher = await copy.Get<IPublisherRepository>().FindByNameAsync("order-created", cancellationToken);
        Assert.Equal(originalPublisher!.PayloadOverride, publisher!.PayloadOverride);
        Assert.Equal((60, false), (publisher.IntervalSeconds, publisher.IsEnabled));

        var listBooks = await copy.Get<ITestScenarioRepository>().FindByNameAsync("list-books", cancellationToken);
        Assert.Equal(("0 9 * * 1-5", "Europe/Kyiv", TestScenarioKind.Send), (listBooks!.Schedule, listBooks.ScheduleTimeZone, listBooks.Kind));
        var settlements = await copy.Get<ITestScenarioRepository>().FindByNameAsync("settlements", cancellationToken);
        Assert.Equal((TestScenarioKind.Listen, 120), (settlements!.Kind, settlements.ListenTimeoutSeconds));

        // brokerOptions and the deprecated exchange both come back; the export writes the v1
        // "exchange" field, so the file stays readable by older images (ADR 0003).
        Assert.Contains("exchange: \"shop\"", manifest);
        Assert.Contains("exchange: \"shop.events\"", manifest);
        Assert.Equal("shop", (await copy.Get<IPublisherRepository>().FindByNameAsync("order-created-rabbit", cancellationToken))!.BrokerOptions?["exchange"]);
        Assert.Equal("shop.events", (await copy.Get<ITestScenarioRepository>().FindByNameAsync("settlements-rabbit", cancellationToken))!.BrokerOptions?["exchange"]);

        var suite = await copy.Get<ITestSuiteRepository>().FindByNameAsync("contract", cancellationToken);
        Assert.True(suite!.RunOnStartup);
        Assert.Equal([settlements.Id, listBooks.Id], suite.TestScenarioIds);
    }

    [Fact]
    public async Task Export_WithInlineValues_KeepsThem_AndLeavesOutWhatNoLongerResolves()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var source = await StartAsync(_sourceRoot, new() { ["ConnectionStrings:kafka"] = "broker:9092" });
        await source.Send(new ApplyProvisioning());
        var kafka = await source.Get<IConnectionRepository>().FindByNameAsync("kafka", cancellationToken);
        await source.Send(new DeleteConnection(kafka!.Id));

        var exported = Unzip(await source.Send(new ExportProvisioning(InlineConnectionValues: true)));
        var manifest = await File.ReadAllTextAsync(Path.Combine(exported, "vroksnet.yaml"), cancellationToken);

        Assert.Contains("value: \"http://bookstore:8080/v1?api-key=secret\"", manifest);
        Assert.Contains("Publisher \"order-created\" isn't exported: its connection no longer exists.", manifest);
        Assert.Contains("Test scenario \"settlements\" isn't exported: its connection no longer exists.", manifest);
        Assert.Contains("Test suite \"contract\" is exported without 1 scenario(s) that aren't.", manifest);
        Assert.Equal(["bookstore-sample-api.yaml", "shop-events-kafka-sample.yaml"], Directory.GetFiles(Path.Combine(exported, "specs")).Select(Path.GetFileName).Order());

        // What's left still provisions cleanly.
        var report = await (await StartAsync(exported, [])).Send(new ApplyProvisioning());
        Assert.True(report.Status == ProvisioningStatus.Applied, string.Join("\n", report.Errors.Select(e => $"{e.Source}: {e.Message}")));
        Assert.Equal(new ProvisioningCounts(2, 2, 1, 2, 1), report.Counts); // the RabbitMQ connection and what uses it are still there
    }

    private async Task<Instance> StartAsync(string provisioningPath, Dictionary<string, string?> settings)
    {
        var dbPath = TempPath() + ".db";
        settings["ConnectionStrings:VroksNetDb"] = $"Data Source={dbPath}";
        settings["Provisioning:Path"] = provisioningPath;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(configuration);
        var provider = services.BuildServiceProvider();
        await provider.InitializeDatabaseAsync();
        // Only the write queue's consumer, as in ApplyProvisioningTests.
        var writes = ActivatorUtilities.CreateInstance<DbWriteBackgroundService>(provider);
        await writes.StartAsync(CancellationToken.None);

        var instance = new Instance(provider, writes, dbPath);
        _instances.Add(instance);
        return instance;
    }

    private string Unzip(byte[] zip)
    {
        var directory = Path.Combine(_sourceRoot, $"export-{Guid.NewGuid():N}");
        using var archive = new ZipArchive(new MemoryStream(zip));
        archive.ExtractToDirectory(directory);
        return directory;
    }

    private static string TempPath() => Path.Combine(Path.GetTempPath(), $"vroksnet-export-{Guid.NewGuid():N}");

    private sealed record Instance(ServiceProvider Services, IHostedService Writes, string DbPath) : IAsyncDisposable
    {
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request)
        {
            await using var scope = Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(request, TestContext.Current.CancellationToken);
        }

        public T Get<T>() where T : notnull => Services.CreateScope().ServiceProvider.GetRequiredService<T>();

        public async ValueTask DisposeAsync()
        {
            await Writes.StopAsync(CancellationToken.None);
            await Services.DisposeAsync();
        }
    }
}
