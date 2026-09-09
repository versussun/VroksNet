using VroksNet.Domain.Connections;

namespace VroksNet.Application.Connections.ListConnections;

public sealed record ConnectionSummary(
    Guid Id,
    string Name,
    ConnectionServiceType ServiceType,
    string Value,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
