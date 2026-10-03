namespace VroksNet.Web;

public sealed record SuiteRunEntry(Guid TestScenarioId, string ScenarioName, TestScenarioKind? Kind, Guid? TestRunId, TestRunStatus Status, string? Message);
