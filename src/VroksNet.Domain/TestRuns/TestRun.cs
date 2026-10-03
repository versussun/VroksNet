namespace VroksNet.Domain.TestRuns;

/// <summary>
/// One run of a <see cref="TestScenarios.TestScenario"/> — the run history ADR 0002 adds next to
/// the scenario's own last-run fields. No foreign-key constraint to the scenario, like the
/// scenario's own references: deleting a scenario leaves its history behind.
/// </summary>
public sealed class TestRun
{
    public Guid Id { get; set; }

    public Guid TestScenarioId { get; set; }

    public TestRunStatus Status { get; set; }

    public TestRunTrigger Trigger { get; set; }

    /// <summary>When the run should start — for an immediate run, when it was queued. Runs are listed newest first by this.</summary>
    public DateTimeOffset ScheduledFor { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>The outcome, safe to show verbatim (same contract as the scenario's <c>LastRunMessage</c>). Null until the run finishes.</summary>
    public string? Message { get; set; }

    /// <summary>The HTTP status of the response, for an HTTP send that got one.</summary>
    public int? StatusCode { get; set; }

    /// <summary>Whether the response or message matched the spec — null when nothing was validated.</summary>
    public bool? ContractValid { get; set; }

    /// <summary>JSON array of contract violations (UI-safe strings) — null unless <see cref="ContractValid"/> is false.</summary>
    public string? ValidationErrors { get; set; }

    public bool IsFinished => Status is TestRunStatus.Passed or TestRunStatus.Failed or TestRunStatus.Cancelled or TestRunStatus.Interrupted;
}
