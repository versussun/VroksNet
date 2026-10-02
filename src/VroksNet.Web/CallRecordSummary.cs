namespace VroksNet.Web;

/// <summary>A call-history row. The request/response bodies aren't included — see <see cref="CallRecordApiClient.GetDetailsAsync"/>.</summary>
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
    string[]? ValidationErrors,
    string[]? Warnings);
