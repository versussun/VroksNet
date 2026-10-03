using VroksNet.Domain.TestRuns;

namespace VroksNet.Application.TestRuns;

/// <summary>One run, as the API returns it. The traffic it produced is in the call history, filtered by <c>testRunId</c>.</summary>
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
    IReadOnlyList<string> ValidationErrors);
