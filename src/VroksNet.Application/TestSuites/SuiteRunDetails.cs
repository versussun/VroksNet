using VroksNet.Domain.TestRuns;
using VroksNet.Domain.TestScenarios;

namespace VroksNet.Application.TestSuites;

/// <summary>
/// One suite run, as the API returns it — what a CI pipeline polls. <see cref="Failed"/> names the
/// scenarios whose run didn't pass (empty while it's still going, or when everything passed).
/// </summary>
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
    IReadOnlyList<SuiteRunEntry> Runs,
    IReadOnlyList<string> Failed);

/// <summary>One scenario's part in a suite run. <see cref="TestRunId"/> is null until the suite run has started.</summary>
public sealed record SuiteRunEntry(
    Guid TestScenarioId,
    string ScenarioName,
    TestScenarioKind? Kind,
    Guid? TestRunId,
    TestRunStatus Status,
    string? Message);
