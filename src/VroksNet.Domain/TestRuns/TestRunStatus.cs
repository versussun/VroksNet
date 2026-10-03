namespace VroksNet.Domain.TestRuns;

/// <summary>Where a <see cref="TestRun"/> is in its life. <c>Queued → Running → Passed / Failed / Cancelled / Interrupted</c>; a queued run can also go straight to <see cref="Cancelled"/>.</summary>
public enum TestRunStatus
{
    Queued,
    Running,
    Passed,
    Failed,

    /// <summary>Someone cancelled it — before it started, or while it ran.</summary>
    Cancelled,

    /// <summary>The app stopped while it ran: it was cut off by shutdown, or found still <see cref="Running"/> at the next start.</summary>
    Interrupted
}
