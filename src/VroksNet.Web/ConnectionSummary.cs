namespace VroksNet.Web;

public sealed record ConnectionSummary(
    Guid Id,
    string Name,
    ConnectionServiceType ServiceType,
    string Value,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ProvisionedAt);
