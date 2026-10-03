namespace VroksNet.Domain.TestRuns;

/// <summary>
/// What started a <see cref="TestRun"/>. Only <see cref="Manual"/> exists so far — a person
/// pressing Run (synchronously or in the background) or the equivalent API call. Schedules,
/// delayed runs and startup suites (ADR 0002) add their own values when they arrive.
/// </summary>
public enum TestRunTrigger
{
    Manual
}
