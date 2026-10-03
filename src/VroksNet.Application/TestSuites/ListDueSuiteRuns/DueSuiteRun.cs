namespace VroksNet.Application.TestSuites.ListDueSuiteRuns;

/// <summary>A queued suite run that's due; the worker runs at most one run per suite at a time.</summary>
public sealed record DueSuiteRun(Guid SuiteRunId, Guid TestSuiteId);
