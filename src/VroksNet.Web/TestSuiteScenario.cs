namespace VroksNet.Web;

/// <summary>A scenario in a suite; a null <see cref="Kind"/> means it no longer exists.</summary>
public sealed record TestSuiteScenario(Guid Id, string Name, TestScenarioKind? Kind);
