using VroksNet.Application.Abstractions;
using VroksNet.Domain.TestRuns;

namespace VroksNet.UnitTests.TestDoubles;

/// <summary>In-memory <see cref="ITestRunRepository"/> with the real one's conditional state changes.</summary>
internal sealed class FakeTestRunRepository : ITestRunRepository
{
    private readonly List<TestRun> _runs = [];

    public IReadOnlyList<TestRun> All => _runs;

    public Task InsertAsync(TestRun run, CancellationToken cancellationToken)
    {
        _runs.Add(run);
        return Task.CompletedTask;
    }

    public Task<TestRun?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
        => Task.FromResult(_runs.FirstOrDefault(run => run.Id == id));

    public Task<IReadOnlyList<TestRun>> ListAsync(TestRunFilter filter, TestRunCursor? after, int limit, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<TestRun>>(_runs
            .Where(run => filter.TestScenarioId is null || run.TestScenarioId == filter.TestScenarioId)
            .Where(run => filter.Status is null || run.Status == filter.Status)
            .Where(run => after is null || run.ScheduledFor < after.ScheduledFor || (run.ScheduledFor == after.ScheduledFor && run.Id.CompareTo(after.Id) < 0))
            .OrderByDescending(run => run.ScheduledFor).ThenByDescending(run => run.Id)
            .Take(limit)
            .ToList());

    public Task<IReadOnlyList<TestRun>> ListDueAsync(DateTimeOffset now, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<TestRun>>(_runs.Where(run => run.Status == TestRunStatus.Queued && run.ScheduledFor <= now).OrderBy(run => run.ScheduledFor).ToList());

    public Task<bool> MarkRunningAsync(Guid id, DateTimeOffset startedAt, CancellationToken cancellationToken)
        => Transition(id, TestRunStatus.Queued, run =>
        {
            run.Status = TestRunStatus.Running;
            run.StartedAt = startedAt;
        });

    public Task<bool> CompleteAsync(Guid id, TestRunOutcome outcome, CancellationToken cancellationToken)
        => Transition(id, TestRunStatus.Running, run =>
        {
            run.Status = outcome.Status;
            run.FinishedAt = outcome.FinishedAt;
            run.Message = outcome.Message;
            run.StatusCode = outcome.StatusCode;
            run.ContractValid = outcome.ContractValid;
            run.ValidationErrors = outcome.ValidationErrors;
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

    public Task<int> PruneAsync(int keepPerScenario, CancellationToken cancellationToken)
    {
        var stale = _runs
            .Where(run => run.IsFinished)
            .GroupBy(run => run.TestScenarioId)
            .SelectMany(group => group.OrderByDescending(run => run.ScheduledFor).ThenByDescending(run => run.Id).Skip(keepPerScenario))
            .ToList();
        _runs.RemoveAll(stale.Contains);
        return Task.FromResult(stale.Count);
    }

    private Task<bool> Transition(Guid id, TestRunStatus expected, Action<TestRun> apply)
    {
        var run = _runs.FirstOrDefault(run => run.Id == id && run.Status == expected);
        if (run is not null)
        {
            apply(run);
        }

        return Task.FromResult(run is not null);
    }
}
