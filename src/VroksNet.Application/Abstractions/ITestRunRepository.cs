using VroksNet.Domain.TestRuns;

namespace VroksNet.Application.Abstractions;

/// <summary>
/// Persistence for <see cref="TestRun"/>s (ADR 0002). Every state change is conditional on the
/// state it expects, so a cancel racing a start, or a late completion of a cancelled run, changes
/// nothing — the returned bool says whether it applied.
/// </summary>
public interface ITestRunRepository
{
    Task InsertAsync(TestRun run, CancellationToken cancellationToken);

    Task<TestRun?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Newest first by (<see cref="TestRun.ScheduledFor"/>, <see cref="TestRun.Id"/>), starting after <paramref name="after"/> when given.</summary>
    Task<IReadOnlyList<TestRun>> ListAsync(TestRunFilter filter, TestRunCursor? after, int limit, CancellationToken cancellationToken);

    /// <summary>Queued runs whose <see cref="TestRun.ScheduledFor"/> is at or before <paramref name="now"/>, oldest first.</summary>
    Task<IReadOnlyList<TestRun>> ListDueAsync(DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Queued → Running. False if the run isn't queued any more (e.g. it was cancelled).</summary>
    Task<bool> MarkRunningAsync(Guid id, DateTimeOffset startedAt, CancellationToken cancellationToken);

    /// <summary>Running → a final status, with the outcome. False if the run isn't running.</summary>
    Task<bool> CompleteAsync(Guid id, TestRunOutcome outcome, CancellationToken cancellationToken);

    /// <summary>Queued → Cancelled. False if the run isn't queued.</summary>
    Task<bool> CancelQueuedAsync(Guid id, DateTimeOffset cancelledAt, CancellationToken cancellationToken);

    /// <summary>Every Running run → Interrupted. For startup: anything still running then was cut off by the previous shutdown.</summary>
    Task<int> InterruptRunningAsync(DateTimeOffset at, string message, CancellationToken cancellationToken);

    /// <summary>Deletes all but the newest <paramref name="keepPerScenario"/> finished runs of each scenario.</summary>
    Task<int> PruneAsync(int keepPerScenario, CancellationToken cancellationToken);
}
