using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VroksNet.Infrastructure.Persistence;

namespace VroksNet.UnitTests.Persistence;

/// <summary>
/// Runs the real migrations against a temp-file SQLite database, covering rows that already
/// existed before a migration added a column — the case <c>EnsureCreated</c>-based tests can't see.
/// </summary>
public sealed class MigrationTests : IAsyncLifetime
{
    private const string MigrationBeforeResponseSchemasByStatus = "20260909113627_AddTestScenarioLastRun";

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"vroksnet-migration-test-{Guid.NewGuid():N}.db");

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync()
    {
        // Microsoft.Data.Sqlite pools the native connection, so it can still hold the file open.
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task AddResponseSchemasByStatus_ExistingEndpointRow_ReadsBackAsEmptyDictionary()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var context = new VroksNetDbContext(new DbContextOptionsBuilder<VroksNetDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        var migrator = context.GetService<IMigrator>();

        await migrator.MigrateAsync(MigrationBeforeResponseSchemasByStatus, cancellationToken: cancellationToken);
        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO ApiSpecifications (Id, Title, Kind, RawContent, CreatedAt, UpdatedAt)
            VALUES ('6F9619FF-8B86-D011-B42D-00C04FC964FF', 'Old spec', 0, 'raw', '2026-09-01 00:00:00+00:00', '2026-09-01 00:00:00+00:00');
            INSERT INTO MockEndpoints (Id, SpecificationId, OperationKey, IsEnabled)
            VALUES ('7F9619FF-8B86-D011-B42D-00C04FC964FF', '6F9619FF-8B86-D011-B42D-00C04FC964FF', 'GET /pets', 1);
            """,
            cancellationToken);

        await migrator.MigrateAsync(cancellationToken: cancellationToken);

        var endpoint = Assert.Single(await context.MockEndpoints.AsNoTracking().ToListAsync(cancellationToken));
        Assert.Equal("GET /pets", endpoint.OperationKey);
        Assert.Empty(endpoint.ResponseSchemasByStatus);
    }
}
