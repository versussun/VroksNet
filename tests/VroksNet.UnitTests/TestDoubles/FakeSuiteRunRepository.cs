using VroksNet.Application.Abstractions;
using VroksNet.Domain.TestRuns;
using VroksNet.Domain.TestSuites;

namespace VroksNet.UnitTests.TestDoubles;

/// <summary>In-memory <see cref="ISuiteRunRepository"/> with the real one's conditional state changes.</summary>
internal sealed class FakeSuiteRunRepository : ISuiteRunRepository
{
    private readonly List<SuiteRun> _runs = [];

    public IReadOnlyList<SuiteRun> All => _runs;

    public Task InsertAsync(SuiteRun run, CancellationToken cancellationToken)
    {
        _runs.Add(run);
        return Task.CompletedTask;
    }

    public Task<SuiteRun?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
        => Task.FromResult(_runs.FirstOrDefault(run => run.Id == id));

    public Task<IReadOnlyList<SuiteRun>> ListAsync(Guid testSuiteId, int limit, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<SuiteRun>>(_runs.Where(run => run.TestSuiteId == testSuiteId).OrderByDescending(run => run.ScheduledFor).Take(limit).ToList());

    public Task<IReadOnlyList<SuiteRun>> ListDueAsync(DateTimeOffset now, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<SuiteRun>>(_runs.Where(run => run.Status == TestRunStatus.Queued && run.ScheduledFor <= now).OrderBy(run => run.ScheduledFor).ToList());

    public Task<bool> MarkRunningAsync(Guid id, DateTimeOffset startedAt, CancellationToken cancellationToken)
        => Transition(id, TestRunStatus.Queued, run =>
        {
            run.Status = TestRunStatus.Running;
            run.StartedAt = startedAt;
        });

    public Task<bool> CompleteAsync(Guid id, TestRunStatus status, DateTimeOffset finishedAt, string message, CancellationToken cancellationToken)
        => Transition(id, TestRunStatus.Running, run =>
        {
            run.Status = status;
            run.FinishedAt = finishedAt;
            run.Message = message;
        });

    public Task<bool> CancelQueuedAsync(Guid id, DateTimeOffset cancelledAt, CancellationToken cancellationToken)
        => Transition(id, TestRunStatus.Queued, run =>
        {
            run.Status = TestRunStatus.Cancelled;
            run.FinishedAt = cancelledAt;
        });

    public Task<int> InterruptRunningAsync(DateTimeOffset at, string message, CancellationToken cancellationToken)
    {
        var running = _runs.Where(run => run.Status == TestRunStatus.Running).ToList();
        foreach (var run in running)
        {
            run.Status = TestRunStatus.Interrupted;
            run.FinishedAt = at;
            run.Message = message;
        }

        return Task.FromResult(running.Count);
    }

    public Task<int> PruneAsync(int keepPerSuite, CancellationToken cancellationToken) => Task.FromResult(0);

    private Task<bool> Transition(Guid id, TestRunStatus expected, Action<SuiteRun> apply)
    {
        var run = _runs.FirstOrDefault(run => run.Id == id && run.Status == expected);
        if (run is not null)
        {
            apply(run);
        }

        return Task.FromResult(run is not null);
    }
}
