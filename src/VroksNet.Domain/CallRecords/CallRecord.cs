namespace VroksNet.Domain.CallRecords;

/// <summary>
/// A logged inbound REST call or outbound broker publish, for the call-history/debugging view
/// (see docs/project-brief.md section 2 "История вызовов").
/// </summary>
public sealed class CallRecord
{
    public Guid Id { get; set; }

    public Guid? SpecificationId { get; set; }

    public Guid? MockEndpointId { get; set; }

    /// <summary>The <see cref="Connections.Connection"/> a <see cref="TestScenarios.TestScenario"/> run sent through, if any — null for an ordinary inbound mock call.</summary>
    public Guid? ConnectionId { get; set; }

    public CallDirection Direction { get; set; }

    public DateTimeOffset Timestamp { get; set; }

    public string? RequestSnapshot { get; set; }

    public string? ResponseSnapshot { get; set; }

    /// <summary>The HTTP status code of the response, for an HTTP call that got one — null for a broker publish or a call that failed before any response arrived.</summary>
    public int? StatusCode { get; set; }

    /// <summary>The <see cref="TestScenarios.TestScenario"/> whose run produced this record, if any — null for an ordinary inbound mock call.</summary>
    public Guid? TestScenarioId { get; set; }

    /// <summary>Whether the call matched the spec's contract — null when nothing was validated (no schema to validate against, or a call kind that isn't validated).</summary>
    public bool? ContractValid { get; set; }

    /// <summary>JSON array of the contract violations found (UI-safe strings) — null unless <see cref="ContractValid"/> is false.</summary>
    public string? ValidationErrors { get; set; }
}
