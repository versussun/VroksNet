namespace VroksNet.Web;

/// <summary>One run of a test scenario, as GET /api/test-runs returns it.</summary>
public sealed record TestRunSummary(
    Guid Id,
    Guid TestScenarioId,
    TestRunStatus Status,
    TestRunTrigger Trigger,
    DateTimeOffset ScheduledFor,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? Message,
    int? StatusCode,
    bool? ContractValid,
    string[] ValidationErrors)
{
    public bool IsFinished => Status is TestRunStatus.Passed or TestRunStatus.Failed or TestRunStatus.Cancelled or TestRunStatus.Interrupted;

    /// <summary>The run's result in the shape the page already shows for a synchronous run.</summary>
    public RunTestScenarioResult ToRunResult() => new(
        Status == TestRunStatus.Passed,
        Message ?? Status.ToString(),
        ResponseBody: null,
        StatusCode,
        ContractValid is { } valid ? new ContractValidationResult(valid, ValidationErrors) : null);
}
