namespace VroksNet.Web;

public sealed record TestScenarioSummary(
    Guid Id,
    string Name,
    Guid SpecificationId,
    string SpecificationTitle,
    Guid MockEndpointId,
    string OperationKey,
    Guid ConnectionId,
    string ConnectionName,
    ConnectionServiceType ConnectionServiceType,
    string? PayloadOverride,
    TestScenarioKind Kind,
    int? ListenTimeoutSeconds,
    string? Exchange,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastRunAt,
    bool? LastRunSuccess,
    string? LastRunMessage);
