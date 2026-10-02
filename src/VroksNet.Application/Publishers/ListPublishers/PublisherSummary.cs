using VroksNet.Domain.Connections;

namespace VroksNet.Application.Publishers.ListPublishers;

/// <summary>A denormalized projection for the list view — display names resolved from whatever the specification/operation/connection currently are, or "(deleted ...)".</summary>
public sealed record PublisherSummary(
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
    string? Exchange,
    int IntervalSeconds,
    bool IsEnabled,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastPublishedAt,
    bool? LastPublishSuccess,
    string? LastPublishMessage);
