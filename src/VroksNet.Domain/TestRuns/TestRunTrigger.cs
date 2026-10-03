namespace VroksNet.Domain.TestRuns;

/// <summary>
/// What started a <see cref="TestRun"/>. Startup suites (ADR 0002) add their own value when they arrive.
/// </summary>
public enum TestRunTrigger
{
    /// <summary>A person pressing Run (synchronously or in the background), or the equivalent API call.</summary>
    Manual,

    /// <summary>The scenario's cron <see cref="TestScenarios.TestScenario.Schedule"/>, queued by the background worker.</summary>
    Schedule,

    /// <summary>A one-off background run someone asked for at a later time (<c>runAt</c> or <c>delaySeconds</c>).</summary>
    Delayed
}
