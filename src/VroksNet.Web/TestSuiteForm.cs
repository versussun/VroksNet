namespace VroksNet.Web;

/// <summary>The body of a test suite create/update.</summary>
public sealed record TestSuiteForm(string Name, Guid[] TestScenarioIds, bool RunOnStartup);
