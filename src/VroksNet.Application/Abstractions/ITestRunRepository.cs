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

    /// <summary>Queued runs whose <see cref="TestRun.ScheduledFor"/> is at or before <paramref name="now"/>, oldest first — not a suite's runs, which their suite run starts.</summary>
    Task<IReadOnlyList<TestRun>> ListDueAsync(DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Queued → Running. False if the run isn't queued any more (e.g. it was cancelled).</summary>
    Task<bool> MarkRunningAsync(Guid id, DateTimeOffset startedAt, CancellationToken cancellationToken);

    /// <summary>Running → a final status, with the outcome. False if the run isn't running.</summary>
    Task<bool> CompleteAsync(Guid id, TestRunOutcome outcome, CancellationToken cancellationToken);

    /// <summary>Queued → Cancelled. False if the run isn't queued.</summary>
    Task<bool> CancelQueuedAsync(Guid id, DateTimeOffset cancelledAt, CancellationToken cancellationToken);

    /// <summary>Every Running run → Interrupted. For startup: anything still running then was cut off by the previous shutdown.</summary>
    Task<int> InterruptRunningAsync(DateTimeOffset at, string message, CancellationToken cancellationToken);

    /// <summary>The runs of one suite run, in the order they were queued.</summary>
    Task<IReadOnlyList<TestRun>> ListBySuiteRunAsync(Guid suiteRunId, CancellationToken cancellationToken);

    /// <summary>A suite run's runs still queued → Cancelled with <paramref name="message"/> (the suite run stopped before reaching them).</summary>
    Task<int> CancelQueuedBySuiteRunAsync(Guid suiteRunId, DateTimeOffset at, string message, CancellationToken cancellationToken);

    /// <summary>The scenario's scheduled run (<see cref="TestRunTrigger.Schedule"/>) with the latest <see cref="TestRun.ScheduledFor"/>, in any status — what the next one is planned from.</summary>
    Task<TestRun?> FindLatestScheduledAsync(Guid testScenarioId, CancellationToken cancellationToken);

    /// <summary>Deletes the scenario's queued scheduled runs — they never ran, so they aren't history. For a schedule that changed or a scenario that's gone.</summary>
    Task<int> DeleteQueuedScheduledAsync(Guid testScenarioId, CancellationToken cancellationToken);

    /// <summary>Queued scheduled runs due before <paramref name="before"/> → Cancelled with <paramref name="message"/>. For startup: they were missed while the app was down.</summary>
    Task<int> SkipQueuedScheduledAsync(DateTimeOffset before, DateTimeOffset at, string message, CancellationToken cancellationToken);

    /// <summary>Deletes all but the newest <paramref name="keepPerScenario"/> finished runs of each scenario.</summary>
    Task<int> PruneAsync(int keepPerScenario, CancellationToken cancellationToken);
}
