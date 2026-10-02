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
    private const string MigrationBeforeCallHistory = "20261001221321_AddResponseSchemasByStatusAndCallRecordContract";

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

    [Fact]
    public async Task AddCallHistoryIndexesAndRequestLine_ConvertsExistingTextTimestampsToComparableTicks()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var context = new VroksNetDbContext(new DbContextOptionsBuilder<VroksNetDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        var migrator = context.GetService<IMigrator>();

        // Written as EF stored them before the migration: text, one with a non-UTC offset that
        // is nonetheless the *earlier* instant (12:00Z vs 12:30Z).
        await migrator.MigrateAsync(MigrationBeforeCallHistory, cancellationToken: cancellationToken);
        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO CallRecords (Id, Direction, Timestamp) VALUES ('11111111-1111-1111-1111-111111111111', 0, '2026-10-01 14:00:00+02:00');
            INSERT INTO CallRecords (Id, Direction, Timestamp) VALUES ('22222222-2222-2222-2222-222222222222', 0, '2026-10-01 12:30:00.5+00:00');
            """,
            cancellationToken);

        await migrator.MigrateAsync(cancellationToken: cancellationToken);

        var records = await context.CallRecords.AsNoTracking().OrderByDescending(record => record.Timestamp).ToListAsync(cancellationToken);
        Assert.Equal(2, records.Count);
        AssertClose(new DateTimeOffset(2026, 10, 1, 12, 30, 0, 500, TimeSpan.Zero), records[0].Timestamp);
        AssertClose(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero), records[1].Timestamp);
    }

    private static void AssertClose(DateTimeOffset expected, DateTimeOffset actual)
        => Assert.True((expected - actual).Duration() < TimeSpan.FromMilliseconds(1), $"Expected {expected:O}, got {actual:O}.");
}
