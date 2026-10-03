using VroksNet.Domain.TestScenarios;

namespace VroksNet.Application.TestSuites;

/// <summary>A suite as listed: its scenarios by name (in order) and its latest run, if any.</summary>
public sealed record TestSuiteSummary(
    Guid Id,
    string Name,
    IReadOnlyList<TestSuiteScenario> Scenarios,
    DateTimeOffset UpdatedAt,
    SuiteRunDetails? LastRun);

/// <summary>A scenario in a suite; "(deleted scenario)" with a null <see cref="Kind"/> if it no longer exists.</summary>
public sealed record TestSuiteScenario(Guid Id, string Name, TestScenarioKind? Kind);
