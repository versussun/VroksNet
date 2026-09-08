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

    public CallDirection Direction { get; set; }

    public DateTimeOffset Timestamp { get; set; }

    public string? RequestSnapshot { get; set; }

    public string? ResponseSnapshot { get; set; }
}
