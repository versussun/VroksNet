namespace VroksNet.Application.TestRuns.ListDueTestRuns;

/// <summary>A queued run that's due; the worker uses <see cref="TestScenarioId"/> to run at most one run per scenario at a time.</summary>
public sealed record DueTestRun(Guid RunId, Guid TestScenarioId);
