namespace VroksNet.Web;

/// <summary>A test suite as GET /api/test-suites lists it.</summary>
public sealed record TestSuiteSummary(
    Guid Id,
    string Name,
    TestSuiteScenario[] Scenarios,
    bool RunOnStartup,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ProvisionedAt,
    SuiteRunDetails? LastRun);
