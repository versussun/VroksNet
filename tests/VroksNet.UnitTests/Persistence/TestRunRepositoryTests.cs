using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using VroksNet.Application.Abstractions;
using VroksNet.Domain.TestRuns;
using VroksNet.Infrastructure.Persistence;

namespace VroksNet.UnitTests.Persistence;

/// <summary>
/// <see cref="TestRunRepository"/> against a real (temp-file) SQLite database through the actual
/// write queue: the conditional state changes the worker and the cancel endpoint race on, the
/// tick-stored times the "due" query and the history order rely on, and history pruning.
/// </summary>
public sealed class TestRunRepositoryTests : IAsyncLifetime
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"vroksnet-test-{Guid.NewGuid():N}.db");
    private TestDbContextFactory _contextFactory = null!;
    private DbWriteBackgroundService _writeService = null!;
    private TestRunRepository _repository = null!;

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
        _repository = new TestRunRepository(_contextFactory, writeQueue);
    }

    public async ValueTask DisposeAsync()
    {
        await _writeService.StopAsync(CancellationToken.None);
        _writeService.Dispose();
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }

    [Fact]
    public async Task ListDueAsync_ReturnsQueuedRunsAtOrBeforeNow_OldestFirst()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenarioId = Guid.NewGuid();
        var later = await InsertAsync(scenarioId, TestRunStatus.Queued, T0.AddSeconds(2));
        var earlier = await InsertAsync(scenarioId, TestRunStatus.Queued, T0);
        await InsertAsync(scenarioId, TestRunStatus.Queued, T0.AddMinutes(5));
        await InsertAsync(scenarioId, TestRunStatus.Running, T0);

        var due = await _repository.ListDueAsync(T0.AddSeconds(2), cancellationToken);

        Assert.Equal([earlier.Id, later.Id], due.Select(run => run.Id));
    }

    [Fact]
    public async Task StateChanges_OnlyApplyFromTheExpectedState()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var run = await InsertAsync(Guid.NewGuid(), TestRunStatus.Queued, T0);

        Assert.False(await _repository.CompleteAsync(run.Id, new TestRunOutcome(TestRunStatus.Passed, T0, "too early"), cancellationToken));
        Assert.True(await _repository.MarkRunningAsync(run.Id, T0.AddSeconds(1), cancellationToken));
        Assert.False(await _repository.MarkRunningAsync(run.Id, T0.AddSeconds(2), cancellationToken));
        Assert.False(await _repository.CancelQueuedAsync(run.Id, T0.AddSeconds(2), cancellationToken));
        Assert.True(await _repository.CompleteAsync(run.Id, new TestRunOutcome(TestRunStatus.Failed, T0.AddSeconds(3), "500", 500, false, "[\"bad\"]"), cancellationToken));
        Assert.False(await _repository.CompleteAsync(run.Id, new TestRunOutcome(TestRunStatus.Passed, T0.AddSeconds(4), "late"), cancellationToken));

        var stored = await _repository.FindByIdAsync(run.Id, cancellationToken);
        Assert.NotNull(stored);
        Assert.Equal(TestRunStatus.Failed, stored.Status);
        Assert.Equal(T0.AddSeconds(1), stored.StartedAt);
        Assert.Equal(T0.AddSeconds(3), stored.FinishedAt);
        Assert.Equal(500, stored.StatusCode);
        Assert.False(stored.ContractValid);
        Assert.Equal("[\"bad\"]", stored.ValidationErrors);
    }

    [Fact]
    public async Task CancelQueuedAsync_CancelsOnlyAQueuedRun()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var queued = await InsertAsync(Guid.NewGuid(), TestRunStatus.Queued, T0);

        Assert.True(await _repository.CancelQueuedAsync(queued.Id, T0.AddSeconds(1), cancellationToken));
        Assert.False(await _repository.MarkRunningAsync(queued.Id, T0.AddSeconds(2), cancellationToken));
        Assert.Equal(TestRunStatus.Cancelled, (await _repository.FindByIdAsync(queued.Id, cancellationToken))!.Status);
    }

    [Fact]
    public async Task InterruptRunningAsync_InterruptsEveryRunningRun_AndNothingElse()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var running = await InsertAsync(Guid.NewGuid(), TestRunStatus.Running, T0);
        var queued = await InsertAsync(Guid.NewGuid(), TestRunStatus.Queued, T0);

        Assert.Equal(1, await _repository.InterruptRunningAsync(T0.AddMinutes(1), "restarted", cancellationToken));

        Assert.Equal(TestRunStatus.Interrupted, (await _repository.FindByIdAsync(running.Id, cancellationToken))!.Status);
        Assert.Equal(TestRunStatus.Queued, (await _repository.FindByIdAsync(queued.Id, cancellationToken))!.Status);
    }

    [Fact]
    public async Task ListAsync_PagesNewestFirst_FilteredByScenario()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenarioId = Guid.NewGuid();
        for (var i = 0; i < 5; i++)
        {
            await InsertAsync(scenarioId, TestRunStatus.Passed, T0.AddSeconds(i));
        }

        await InsertAsync(Guid.NewGuid(), TestRunStatus.Passed, T0.AddSeconds(10));

        var filter = new TestRunFilter(TestScenarioId: scenarioId);
        var first = await _repository.ListAsync(filter, null, 2, cancellationToken);
        var second = await _repository.ListAsync(filter, new TestRunCursor(first[^1].ScheduledFor, first[^1].Id), 10, cancellationToken);

        Assert.Equal([T0.AddSeconds(4), T0.AddSeconds(3)], first.Select(run => run.ScheduledFor));
        Assert.Equal([T0.AddSeconds(2), T0.AddSeconds(1), T0], second.Select(run => run.ScheduledFor));
    }

    [Fact]
    public async Task PruneAsync_KeepsTheNewestFinishedRunsPerScenario_AndNeverActiveOnes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var busy = Guid.NewGuid();
        var quiet = Guid.NewGuid();
        for (var i = 0; i < 4; i++)
        {
            await InsertAsync(busy, TestRunStatus.Passed, T0.AddSeconds(i));
        }

        var oldQueued = await InsertAsync(busy, TestRunStatus.Queued, T0.AddSeconds(-10));
        await InsertAsync(quiet, TestRunStatus.Failed, T0);

        Assert.Equal(2, await _repository.PruneAsync(2, cancellationToken));

        var busyRuns = await _repository.ListAsync(new TestRunFilter(busy), null, 10, cancellationToken);
        Assert.Equal([T0.AddSeconds(3), T0.AddSeconds(2), T0.AddSeconds(-10)], busyRuns.Select(run => run.ScheduledFor));
        Assert.Contains(busyRuns, run => run.Id == oldQueued.Id);
        Assert.Single(await _repository.ListAsync(new TestRunFilter(quiet), null, 10, cancellationToken));
    }

    private async Task<TestRun> InsertAsync(Guid scenarioId, TestRunStatus status, DateTimeOffset scheduledFor)
    {
        var run = new TestRun
        {
            Id = Guid.NewGuid(),
            TestScenarioId = scenarioId,
            Status = status,
            Trigger = TestRunTrigger.Manual,
            ScheduledFor = scheduledFor
        };
        await _repository.InsertAsync(run, TestContext.Current.CancellationToken);
        return run;
    }

    private sealed class TestDbContextFactory(string connectionString) : IDbContextFactory<VroksNetDbContext>
    {
        public VroksNetDbContext CreateDbContext()
            => new(new DbContextOptionsBuilder<VroksNetDbContext>().UseSqlite(connectionString).Options);

        public Task<VroksNetDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }
}
