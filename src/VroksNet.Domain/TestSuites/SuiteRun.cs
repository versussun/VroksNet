using VroksNet.Domain.TestRuns;

namespace VroksNet.Domain.TestSuites;

/// <summary>
/// One run of a <see cref="TestSuite"/>: a <see cref="TestRun"/> per scenario, linked by
/// <see cref="TestRun.SuiteRunId"/>. Its <see cref="Status"/> follows the runs' lifecycle; a
/// finished suite run is <see cref="TestRunStatus.Passed"/> only if every run passed.
/// </summary>
public sealed class SuiteRun
{
    public Guid Id { get; set; }

    public Guid TestSuiteId { get; set; }

    public TestRunStatus Status { get; set; }

    public TestRunTrigger Trigger { get; set; }

    public DateTimeOffset ScheduledFor { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>A one-line summary, safe to show verbatim — e.g. "2 of 3 passed". Null until it finishes.</summary>
    public string? Message { get; set; }

    public bool IsFinished => Status is TestRunStatus.Passed or TestRunStatus.Failed or TestRunStatus.Cancelled or TestRunStatus.Interrupted;
}
