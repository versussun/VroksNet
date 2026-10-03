using VroksNet.Domain.Connections;
using VroksNet.Domain.TestScenarios;

namespace VroksNet.Application.TestScenarios.ListTestScenarios;

/// <summary>A lightweight, denormalized projection for list views — display names instead of just the ids, resolved from whatever the specification/operation/connection currently are (or "(deleted ...)" if one no longer exists). <see cref="NextScheduledRunAt"/> is the schedule's next occurrence from now, null without a schedule. <see cref="Exchange"/> is <c>BrokerOptions["exchange"]</c>, kept for contract v1 (deprecated, ADR 0003).</summary>
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
    IReadOnlyDictionary<string, string>? BrokerOptions,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastRunAt,
    bool? LastRunSuccess,
    string? LastRunMessage,
    DateTimeOffset? ProvisionedAt,
    string? Schedule,
    string? ScheduleTimeZone,
    DateTimeOffset? NextScheduledRunAt);
