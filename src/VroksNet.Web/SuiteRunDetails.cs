namespace VroksNet.Web;

/// <summary>One suite run with each scenario's part in it.</summary>
public sealed record SuiteRunDetails(
    Guid Id,
    Guid TestSuiteId,
    string SuiteName,
    TestRunStatus Status,
    TestRunTrigger Trigger,
    DateTimeOffset ScheduledFor,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? Message,
    SuiteRunEntry[] Runs,
    string[] Failed)
{
    public bool IsFinished => Status is TestRunStatus.Passed or TestRunStatus.Failed or TestRunStatus.Cancelled or TestRunStatus.Interrupted;
}
