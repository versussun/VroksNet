using VroksNet.Domain.CallRecords;

namespace VroksNet.Application.CallRecords.ListCallRecords;

/// <summary>
/// A call-history row, denormalized for display: names instead of just ids, resolved from whatever
/// the specification/operation/connection/scenario currently are — null if the call had none, or
/// "(deleted ...)" if it had one that no longer exists. The request/response bodies aren't
/// included — they can be large; GetCallRecord fetches them for one record on demand.
/// </summary>
public sealed record CallRecordSummary(
    Guid Id,
    DateTimeOffset Timestamp,
    CallDirection Direction,
    Guid? SpecificationId,
    string? SpecificationTitle,
    Guid? MockEndpointId,
    string? OperationKey,
    Guid? ConnectionId,
    string? ConnectionName,
    Guid? TestScenarioId,
    string? TestScenarioName,
    string? RequestLine,
    int? StatusCode,
    bool? ContractValid,
    IReadOnlyList<string> ValidationErrors,
    IReadOnlyList<string> Warnings);
