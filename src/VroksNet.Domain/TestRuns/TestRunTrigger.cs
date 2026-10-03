namespace VroksNet.Domain.TestRuns;

/// <summary>
/// What started a <see cref="TestRun"/>. Delayed runs and startup suites (ADR 0002) add their own
/// values when they arrive.
/// </summary>
public enum TestRunTrigger
{
    /// <summary>A person pressing Run (synchronously or in the background), or the equivalent API call.</summary>
    Manual,

    /// <summary>The scenario's cron <see cref="TestScenarios.TestScenario.Schedule"/>, queued by the background worker.</summary>
    Schedule
}
