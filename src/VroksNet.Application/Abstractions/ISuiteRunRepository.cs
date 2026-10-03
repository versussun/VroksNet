using VroksNet.Domain.TestRuns;
using VroksNet.Domain.TestSuites;

namespace VroksNet.Application.Abstractions;

/// <summary>
/// Persistence for <see cref="SuiteRun"/>s. Like <see cref="ITestRunRepository"/>, every state
/// change is conditional on the state it expects, and the returned bool says whether it applied.
/// </summary>
public interface ISuiteRunRepository
{
    Task InsertAsync(SuiteRun run, CancellationToken cancellationToken);

    Task<SuiteRun?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The suite's runs, newest first by <see cref="SuiteRun.ScheduledFor"/>.</summary>
    Task<IReadOnlyList<SuiteRun>> ListAsync(Guid testSuiteId, int limit, CancellationToken cancellationToken);

    /// <summary>Queued suite runs due at <paramref name="now"/>, oldest first.</summary>
    Task<IReadOnlyList<SuiteRun>> ListDueAsync(DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Queued → Running.</summary>
    Task<bool> MarkRunningAsync(Guid id, DateTimeOffset startedAt, CancellationToken cancellationToken);

    /// <summary>Running → a final status.</summary>
    Task<bool> CompleteAsync(Guid id, TestRunStatus status, DateTimeOffset finishedAt, string message, CancellationToken cancellationToken);

    /// <summary>Queued → Cancelled.</summary>
    Task<bool> CancelQueuedAsync(Guid id, DateTimeOffset cancelledAt, CancellationToken cancellationToken);

    /// <summary>Every Running suite run → Interrupted (startup).</summary>
    Task<int> InterruptRunningAsync(DateTimeOffset at, string message, CancellationToken cancellationToken);

    /// <summary>Deletes all but the newest <paramref name="keepPerSuite"/> finished runs of each suite.</summary>
    Task<int> PruneAsync(int keepPerSuite, CancellationToken cancellationToken);
}
