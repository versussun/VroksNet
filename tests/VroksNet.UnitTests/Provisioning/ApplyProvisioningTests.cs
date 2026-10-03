using Mediator;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VroksNet.Application;
using VroksNet.Application.Abstractions;
using VroksNet.Application.Provisioning;
using VroksNet.Application.Provisioning.ApplyProvisioning;
using VroksNet.Application.Publishers.UpdatePublisher;
using VroksNet.Domain.TestScenarios;
using VroksNet.Infrastructure;
using VroksNet.Infrastructure.Persistence;

namespace VroksNet.UnitTests.Provisioning;

/// <summary>
/// Provisioning end to end below the host (ADR 0001, step A3): a real provisioning directory read
/// by FileProvisioningSource (schema validation, valueFrom), applied through the real use cases
/// over Mediator, into a real temp-file SQLite database through the write queue.
/// </summary>
public sealed class ApplyProvisioningTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"vroksnet-provisioning-{Guid.NewGuid():N}");
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"vroksnet-provisioning-{Guid.NewGuid():N}.db");
    private ServiceProvider _services = null!;
    private IHostedService _writeService = null!;

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(_root, "specs", "events"));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:VroksNetDb"] = $"Data Source={_dbPath}",
            ["Provisioning:Path"] = _root,
            ["ConnectionStrings:kafka"] = "localhost:9092"
        }).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(configuration);
        _services = services.BuildServiceProvider();

        await _services.InitializeDatabaseAsync();
        // Only the write queue's consumer: the other hosted services (the workers, provisioning's
        // own startup run) need a real host, and this test drives provisioning itself.
        _writeService = ActivatorUtilities.CreateInstance<DbWriteBackgroundService>(_services);
        await _writeService.StartAsync(CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        await _writeService.StopAsync(CancellationToken.None);
        await _services.DisposeAsync();
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Apply_ImportsSpecsAndCreatesEverythingInTheManifest_ThenAgainChangesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        CopySample("bookstore-openapi.yaml", "specs/bookstore-openapi.yaml");
        CopySample("shop-events-kafka-asyncapi.yaml", "specs/events/shop-events-kafka-asyncapi.yaml");
        WriteManifest(ValidManifest);

        var report = await ApplyAsync();

        Assert.True(report.Status == ProvisioningStatus.Applied, string.Join("\n", report.Errors.Select(e => $"{e.Source}: {e.Message}")));
        Assert.Equal(new ProvisioningCounts(2, 2, 1, 2), report.Counts);

        var specifications = Get<IApiSpecificationRepository>();
        var bookstore = await specifications.FindByTitleAsync("Bookstore Sample API", cancellationToken);
        Assert.NotNull(bookstore?.ProvisionedAt);
        Assert.False(bookstore.Endpoints.Single(e => e.OperationKey == "DELETE /books/{bookId}").IsEnabled);
        Assert.True(bookstore.Endpoints.Single(e => e.OperationKey == "GET /books").IsEnabled);

        var kafka = await Get<IConnectionRepository>().FindByNameAsync("kafka", cancellationToken);
        Assert.Equal("localhost:9092", kafka!.Value); // from valueFrom
        Assert.NotNull(kafka.ProvisionedAt);

        var publisher = await Get<IPublisherRepository>().FindByNameAsync("order-created", cancellationToken);
        Assert.False(publisher!.IsEnabled);
        Assert.Equal(60, publisher.IntervalSeconds);

        var settlements = await Get<ITestScenarioRepository>().FindByNameAsync("settlements", cancellationToken);
        Assert.Equal(TestScenarioKind.Listen, settlements!.Kind); // an AsyncAPI "send" defaults to Listen
        Assert.Equal(120, settlements.ListenTimeoutSeconds);

        // Someone edits a provisioned Publisher in the UI…
        await Send(new UpdatePublisher(publisher.Id, "order-created", publisher.SpecificationId, publisher.MockEndpointId, publisher.ConnectionId, null, 5));

        // …and the next start brings it back, without creating anything twice.
        var again = await ApplyAsync();
        Assert.Equal(ProvisioningStatus.Applied, again.Status);
        Assert.Equal(2, (await specifications.ListAsync(cancellationToken)).Count);
        Assert.Equal(2, (await Get<IConnectionRepository>().ListAsync(cancellationToken)).Count);
        Assert.Equal(2, (await Get<ITestScenarioRepository>().ListAsync(cancellationToken)).Count);
        var reverted = Assert.Single(await Get<IPublisherRepository>().ListAsync(cancellationToken));
        Assert.Equal(publisher.Id, reverted.Id);
        Assert.Equal(60, reverted.IntervalSeconds);
        Assert.Equal(settlements.Id, (await Get<ITestScenarioRepository>().FindByNameAsync("settlements", cancellationToken))!.Id);
    }

    [Fact]
    public async Task Apply_CollectsEveryProblem_AndAppliesWhatItCan()
    {
        CopySample("bookstore-openapi.yaml", "specs/bookstore-openapi.yaml");
        File.WriteAllText(Path.Combine(_root, "specs", "notes.yaml"), "title: not a spec\n");
        WriteManifest("""
            version: 1
            connections:
              - name: bookstore-http
                type: Http
                value: http://bookstore:8080
              - name: rabbit
                type: RabbitMq
                valueFrom: ConnectionStrings:rabbit
            testScenarios:
              - name: list-books
                specification: Bookstore Sample API
                operation: "GET /books"
                connection: bookstore-http
              - name: lost
                specification: Bookstore Sample API
                operation: "GET /nothing-here"
                connection: bookstore-http
              - name: orphan
                specification: No Such Spec
                operation: "GET /books"
                connection: bookstore-http
            """);

        var report = await ApplyAsync();

        Assert.Equal(ProvisioningStatus.Failed, report.Status);
        Assert.Equal(["connections[rabbit]", "specs/notes.yaml", "testScenarios[lost]", "testScenarios[orphan]"], report.Errors.Select(e => e.Source).Order());
        Assert.Contains("ConnectionStrings:rabbit", report.Errors.Single(e => e.Source == "connections[rabbit]").Message);
        Assert.Equal(new ProvisioningCounts(1, 1, 0, 1), report.Counts); // the valid parts still applied
    }

    [Fact]
    public async Task Apply_ManifestThatBreaksTheSchema_IsReportedWithWhereItBreaks()
    {
        WriteManifest("""
            version: 1
            publisher:
              - name: typo-in-the-key
            """);

        var report = await ApplyAsync();

        Assert.Equal(ProvisioningStatus.Failed, report.Status);
        Assert.All(report.Errors, error => Assert.StartsWith("vroksnet.yaml", error.Source));
    }

    [Fact]
    public async Task Apply_WithoutTheDirectory_IsNotConfigured()
    {
        Directory.Delete(_root, recursive: true);

        var report = await ApplyAsync();

        Assert.Equal(ProvisioningStatus.NotConfigured, report.Status);
        Directory.CreateDirectory(_root); // for DisposeAsync
    }

    private const string ValidManifest = """
        version: 1
        connections:
          - name: kafka
            type: Kafka
            valueFrom: ConnectionStrings:kafka
          - name: bookstore-http
            type: Http
            value: http://bookstore:8080
        specifications:
          - title: Bookstore Sample API
            disabledOperations: ["DELETE /books/{bookId}"]
        publishers:
          - name: order-created
            specification: Shop Events Kafka Sample
            operation: "shop.orders.created:send"
            connection: kafka
            intervalSeconds: 60
            enabled: false
        testScenarios:
          - name: list-books
            specification: Bookstore Sample API
            operation: "GET /books"
            connection: bookstore-http
          - name: settlements
            specification: Shop Events Kafka Sample
            operation: "shop.payments.{region}.settled:send"
            connection: kafka
            listenTimeoutSeconds: 120
        """;

    private async Task<ProvisioningReport> ApplyAsync() => await Send(new ApplyProvisioning());

    private async Task<TResponse> Send<TResponse>(IRequest<TResponse> request)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(request, TestContext.Current.CancellationToken);
    }

    private T Get<T>() where T : notnull => _services.CreateScope().ServiceProvider.GetRequiredService<T>();

    private void CopySample(string sample, string relativePath)
        => File.Copy(Path.Combine(AppContext.BaseDirectory, "Samples", sample), Path.Combine(_root, relativePath));

    private void WriteManifest(string yaml) => File.WriteAllText(Path.Combine(_root, "vroksnet.yaml"), yaml);
}
