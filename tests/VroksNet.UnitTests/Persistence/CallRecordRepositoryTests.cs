using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using VroksNet.Application.Abstractions;
using VroksNet.Domain.CallRecords;
using VroksNet.Infrastructure.Persistence;

namespace VroksNet.UnitTests.Persistence;

/// <summary>Real temp-file SQLite through the actual write queue — covers the history's ordering, filters and keyset paging as SQL, not just as LINQ-to-objects.</summary>
public sealed class CallRecordRepositoryTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"vroksnet-test-{Guid.NewGuid():N}.db");
    private TestDbContextFactory _contextFactory = null!;
    private DbWriteBackgroundService _writeService = null!;
    private CallRecordRepository _repository = null!;

    public async ValueTask InitializeAsync()
    {
        _contextFactory = new TestDbContextFactory($"Data Source={_dbPath}");
        await using (var context = await _contextFactory.CreateDbContextAsync())
        {
            await context.Database.EnsureCreatedAsync();
        }

        var writeQueue = new DbWriteQueue();
        _writeService = new DbWriteBackgroundService(writeQueue, _contextFactory, NullLogger<DbWriteBackgroundService>.Instance);
        await _writeService.StartAsync(CancellationToken.None);

        _repository = new CallRecordRepository(_contextFactory, writeQueue);
    }

    public async ValueTask DisposeAsync()
    {
        await _writeService.StopAsync(CancellationToken.None);
        _writeService.Dispose();

        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }

    [Fact]
    public async Task ListAsync_ReturnsNewestFirst_AcrossTimeZoneOffsets()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // Same instant ordering must hold even when offsets differ — a text comparison of
        // "2026-10-01 14:00:00+02:00" vs "2026-10-01 12:30:00+00:00" would get this wrong.
        var older = await InsertAsync(Start.ToOffset(TimeSpan.FromHours(2)));
        var newer = await InsertAsync(Start.AddMinutes(30));

        var page = await _repository.ListAsync(new CallRecordFilter(), null, 10, cancellationToken);

        Assert.Equal([newer.Id, older.Id], page.Select(record => record.Id));
    }

    [Fact]
    public async Task ListAsync_KeysetPaging_NeverSkipsOrRepeatsRecordsWithEqualTimestamps()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var inserted = new List<CallRecord>();
        for (var i = 0; i < 5; i++)
        {
            // Pairs share a timestamp, so the id tie-breaker is what keeps paging exact.
            inserted.Add(await InsertAsync(Start.AddSeconds(i / 2)));
        }

        var seen = new List<Guid>();
        CallRecordCursor? after = null;
        while (true)
        {
            var page = await _repository.ListAsync(new CallRecordFilter(), after, 2, cancellationToken);
            if (page.Count == 0)
            {
                break;
            }

            seen.AddRange(page.Select(record => record.Id));
            after = new CallRecordCursor(page[^1].Timestamp, page[^1].Id);
        }

        Assert.Equal(inserted.Count, seen.Count);
        Assert.Equal(inserted.Select(record => record.Id).ToHashSet(), seen.ToHashSet());

        var timestampsById = inserted.ToDictionary(record => record.Id, record => record.Timestamp);
        var seenTimestamps = seen.Select(id => timestampsById[id]).ToList();
        Assert.Equal(seenTimestamps.OrderByDescending(timestamp => timestamp), seenTimestamps);
    }

    [Fact]
    public async Task ListAsync_FiltersByEveryCriterion()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var specificationId = Guid.NewGuid();
        var scenarioId = Guid.NewGuid();
        var match = await InsertAsync(Start, record =>
        {
            record.SpecificationId = specificationId;
            record.TestScenarioId = scenarioId;
            record.Direction = CallDirection.OutboundHttpRequest;
            record.ContractValid = false;
        });
        await InsertAsync(Start, record => record.SpecificationId = specificationId); // wrong scenario/direction/contract
        await InsertAsync(Start, record => record.ContractValid = false);            // wrong spec

        var page = await _repository.ListAsync(
            new CallRecordFilter(specificationId, null, scenarioId, Direction: CallDirection.OutboundHttpRequest, ContractValid: false), null, 10, cancellationToken);

        Assert.Equal(match.Id, Assert.Single(page).Id);
    }

    [Fact]
    public async Task DeleteAllAsync_RemovesEverythingAndReportsCount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await InsertAsync(Start);
        await InsertAsync(Start.AddSeconds(1));

        var deleted = await _repository.DeleteAllAsync(cancellationToken);

        Assert.Equal(2, deleted);
        Assert.Empty(await _repository.ListAsync(new CallRecordFilter(), null, 10, cancellationToken));
    }

    private async Task<CallRecord> InsertAsync(DateTimeOffset timestamp, Action<CallRecord>? configure = null)
    {
        var record = new CallRecord { Id = Guid.NewGuid(), Timestamp = timestamp, Direction = CallDirection.InboundHttpRequest };
        configure?.Invoke(record);
        await _repository.InsertAsync(record, TestContext.Current.CancellationToken);
        return record;
    }

    private sealed class TestDbContextFactory(string connectionString) : IDbContextFactory<VroksNetDbContext>
    {
        public VroksNetDbContext CreateDbContext()
            => new(new DbContextOptionsBuilder<VroksNetDbContext>().UseSqlite(connectionString).Options);

        public Task<VroksNetDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }
}
