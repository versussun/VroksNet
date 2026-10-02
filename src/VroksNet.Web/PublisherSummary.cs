namespace VroksNet.Web;

/// <summary>A publisher as listed — display names resolved by the server, "(deleted ...)" if a reference no longer exists.</summary>
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
