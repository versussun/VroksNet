using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using VroksNet.Domain.ApiSpecifications;
using VroksNet.Domain.MockEndpoints;
using VroksNet.Infrastructure.Persistence;

namespace VroksNet.UnitTests.Persistence;

/// <summary>
/// Runs against a real (temp-file) SQLite database through the actual write queue + background
/// consumer — coverage for the by-title upsert. It's an idempotent in-place update
/// (<see cref="ApiSpecification.ApplyReimport"/>), done on entities the write context loaded
/// itself: an earlier attach-and-patch of the detached graph threw
/// <see cref="DbUpdateConcurrencyException"/>.
/// </summary>
public sealed class ApiSpecificationRepositoryTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"vroksnet-test-{Guid.NewGuid():N}.db");
    private TestDbContextFactory _contextFactory = null!;
    private DbWriteQueue _writeQueue = null!;
    private DbWriteBackgroundService _writeService = null!;
    private ApiSpecificationRepository _repository = null!;

    public async ValueTask InitializeAsync()
    {
        _contextFactory = new TestDbContextFactory($"Data Source={_dbPath}");
        await using (var context = await _contextFactory.CreateDbContextAsync())
        {
            await context.Database.EnsureCreatedAsync();
        }

        _writeQueue = new DbWriteQueue();
        _writeService = new DbWriteBackgroundService(_writeQueue, _contextFactory, NullLogger<DbWriteBackgroundService>.Instance);
        await _writeService.StartAsync(CancellationToken.None);

        _repository = new ApiSpecificationRepository(_contextFactory, _writeQueue);
    }

    public async ValueTask DisposeAsync()
    {
        await _writeService.StopAsync(CancellationToken.None);
        _writeService.Dispose();

        // Microsoft.Data.Sqlite pools the native connection by default, so it can still hold
        // the file open here even though every DbContext using it has been disposed.
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }

    [Fact]
    public async Task UpsertAsync_NewTitle_IsFoundAfterwards()
    {
        var specification = CreateSpecification("Orders API", "GET /orders");

        await _repository.UpsertAsync(specification, TestContext.Current.CancellationToken);

        var stored = await _repository.FindByTitleAsync("Orders API", TestContext.Current.CancellationToken);
        Assert.NotNull(stored);
        Assert.Equal(specification.Id, stored.Id);
        Assert.Single(stored.Endpoints);
    }

    [Fact]
    public async Task UpsertAsync_SameTitleTwice_ReplacesWithoutThrowing()
    {
        var first = CreateSpecification("Orders API", "GET /orders");
        await _repository.UpsertAsync(first, TestContext.Current.CancellationToken);

        var second = CreateSpecification("Orders API", "GET /orders", "POST /orders", "DELETE /orders/{id}");
        await _repository.UpsertAsync(second, TestContext.Current.CancellationToken);

        var all = await _repository.ListAsync(TestContext.Current.CancellationToken);
        var stored = Assert.Single(all);
        Assert.Equal(3, stored.Endpoints.Count);
    }

    [Fact]
    public async Task UpsertAsync_SameImportAgain_KeepsIdsAndAdminStateAndWritesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var first = CreateSpecification("Orders API", "GET /orders", "POST /orders");
        var storedId = await _repository.UpsertAsync(first, cancellationToken);
        var original = await _repository.FindByTitleAsync("Orders API", cancellationToken);
        Assert.NotNull(original);
        var getOrders = original.Endpoints.Single(e => e.OperationKey == "GET /orders");
        await _repository.SetEnabledAsync([getOrders.Id], enabled: false, cancellationToken);
        await _repository.SetServeAtRealPathAsync([getOrders.Id], serveAtRealPath: true, cancellationToken);

        // A fresh parse of the same file: new ids everywhere, as the import handlers build it.
        var again = CreateSpecification("Orders API", "GET /orders", "POST /orders");
        again.UpdatedAt = original.UpdatedAt.AddHours(1);
        var returnedId = await _repository.UpsertAsync(again, cancellationToken);

        var stored = await _repository.FindByTitleAsync("Orders API", cancellationToken);
        Assert.NotNull(stored);
        Assert.Equal(storedId, returnedId);
        Assert.Equal(storedId, stored.Id);
        Assert.Equal(original.UpdatedAt, stored.UpdatedAt);
        Assert.Equal(
            original.Endpoints.Select(e => e.Id).Order(),
            stored.Endpoints.Select(e => e.Id).Order());
        var kept = stored.Endpoints.Single(e => e.Id == getOrders.Id);
        Assert.False(kept.IsEnabled);
        Assert.True(kept.ServeAtRealPath);
    }

    [Fact]
    public async Task UpsertAsync_ChangedOperation_IsUpdatedInPlace()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var first = CreateSpecification("Orders API", "GET /orders");
        first.Endpoints.Single().ExampleTemplate = "{\"v\":1}";
        await _repository.UpsertAsync(first, cancellationToken);
        var originalId = first.Endpoints.Single().Id;

        var changed = CreateSpecification("Orders API", "GET /orders");
        var incoming = changed.Endpoints.Single();
        incoming.ExampleTemplate = "{\"v\":2}";
        incoming.ExampleStatusCode = 201;
        incoming.ExampleIsGenerated = true; // a spec that dropped its example gets one built from the schema
        incoming.RequestExampleTemplate = "{\"name\":\"Fido\"}";
        incoming.RequestExampleIsGenerated = true;
        incoming.ResponseSchemasByStatus = new Dictionary<string, string?> { ["201"] = "{\"type\":\"object\"}" };
        changed.RawContent = "raw v2";
        await _repository.UpsertAsync(changed, cancellationToken);

        var stored = Assert.Single((await _repository.FindByTitleAsync("Orders API", cancellationToken))!.Endpoints);
        Assert.Equal(originalId, stored.Id);
        Assert.Equal("{\"v\":2}", stored.ExampleTemplate);
        Assert.Equal(201, stored.ExampleStatusCode);
        Assert.True(stored.ExampleIsGenerated);
        Assert.Equal(("{\"name\":\"Fido\"}", true), (stored.RequestExampleTemplate, stored.RequestExampleIsGenerated));
        Assert.Equal("{\"type\":\"object\"}", stored.ResponseSchemasByStatus["201"]);
    }

    [Fact]
    public async Task UpsertAsync_OperationsAddedAndRemoved_KeepsOnlyTheSurvivorsIds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var first = CreateSpecification("Orders API", "GET /orders", "DELETE /orders/{id}");
        await _repository.UpsertAsync(first, cancellationToken);
        var keptId = first.Endpoints.Single(e => e.OperationKey == "GET /orders").Id;

        var next = CreateSpecification("Orders API", "GET /orders", "POST /orders");
        next.RawContent = "raw v2";
        await _repository.UpsertAsync(next, cancellationToken);

        var stored = (await _repository.FindByTitleAsync("Orders API", cancellationToken))!.Endpoints;
        Assert.Equal(["GET /orders", "POST /orders"], stored.Select(e => e.OperationKey).Order());
        Assert.Equal(keptId, stored.Single(e => e.OperationKey == "GET /orders").Id);
        Assert.Equal(
            next.Endpoints.Single(e => e.OperationKey == "POST /orders").Id,
            stored.Single(e => e.OperationKey == "POST /orders").Id);
    }

    [Fact]
    public async Task UpsertAsync_RepeatedOperationKeys_ArePairedByOrder()
    {
        // AsyncAPI allows two operations with the same channel and action, hence the same key.
        var cancellationToken = TestContext.Current.CancellationToken;
        var first = CreateSpecification("Events", "orders.created:send", "orders.created:send");
        first.Endpoints.ElementAt(0).ExampleTemplate = "first";
        first.Endpoints.ElementAt(1).ExampleTemplate = "second";
        await _repository.UpsertAsync(first, cancellationToken);
        var ids = first.Endpoints.Select(e => e.Id).ToList();

        var next = CreateSpecification("Events", "orders.created:send", "orders.created:send");
        next.Endpoints.ElementAt(0).ExampleTemplate = "first";
        next.Endpoints.ElementAt(1).ExampleTemplate = "second, changed";
        await _repository.UpsertAsync(next, cancellationToken);

        var stored = (await _repository.FindByTitleAsync("Events", cancellationToken))!.Endpoints.ToDictionary(e => e.Id);
        Assert.Equal(2, stored.Count);
        Assert.Equal("first", stored[ids[0]].ExampleTemplate);
        Assert.Equal("second, changed", stored[ids[1]].ExampleTemplate);
    }

    [Fact]
    public async Task UpsertAsync_ResponseSchemasByStatus_RoundTripsIncludingNullSchemas()
    {
        var specification = CreateSpecification("Pets API", "GET /pets");
        specification.Endpoints.Single().ResponseSchemasByStatus = new Dictionary<string, string?>
        {
            ["200"] = """{"type":"array"}""",
            ["404"] = null
        };

        await _repository.UpsertAsync(specification, TestContext.Current.CancellationToken);

        var stored = await _repository.FindByTitleAsync("Pets API", TestContext.Current.CancellationToken);
        var schemas = Assert.Single(stored!.Endpoints).ResponseSchemasByStatus;
        Assert.Equal(2, schemas.Count);
        Assert.Equal("""{"type":"array"}""", schemas["200"]);
        Assert.True(schemas.ContainsKey("404"));
        Assert.Null(schemas["404"]);
    }

    /// <summary>R5: a spec stored before protocols were recorded gets them from its next import of the same file.</summary>
    [Fact]
    public async Task UpsertAsync_SameFileWithProtocols_FillsThemIn()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _repository.UpsertAsync(CreateSpecification("Orders API", "orders.created:send"), cancellationToken);

        var again = CreateSpecification("Orders API", "orders.created:send");
        again.Protocols = ["kafka", "kafka-secure"];
        await _repository.UpsertAsync(again, cancellationToken);

        var stored = await _repository.FindByTitleAsync("Orders API", cancellationToken);
        Assert.Equal(["kafka", "kafka-secure"], stored!.Protocols);
    }

    private static ApiSpecification CreateSpecification(string title, params string[] operationKeys)
    {
        var specification = new ApiSpecification
        {
            Id = Guid.NewGuid(),
            Title = title,
            Kind = SpecificationKind.OpenApi,
            RawContent = "raw",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        specification.Endpoints = operationKeys
            .Select((key, position) => new MockEndpoint { Id = Guid.NewGuid(), SpecificationId = specification.Id, OperationKey = key, Position = position })
            .ToList();

        return specification;
    }

    private sealed class TestDbContextFactory(string connectionString) : IDbContextFactory<VroksNetDbContext>
    {
        public VroksNetDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<VroksNetDbContext>().UseSqlite(connectionString).Options;
            return new VroksNetDbContext(options);
        }

        public Task<VroksNetDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }
}
