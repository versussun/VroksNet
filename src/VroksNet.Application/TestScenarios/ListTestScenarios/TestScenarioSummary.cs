using VroksNet.Domain.Connections;
using VroksNet.Domain.TestScenarios;

namespace VroksNet.Application.TestScenarios.ListTestScenarios;

/// <summary>A lightweight, denormalized projection for list views — display names instead of just the ids, resolved from whatever the specification/operation/connection currently are (or "(deleted ...)" if one no longer exists).</summary>
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
    string? ListenExchange,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastRunAt,
    bool? LastRunSuccess,
    string? LastRunMessage);
