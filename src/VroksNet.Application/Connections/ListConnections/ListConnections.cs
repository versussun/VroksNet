using Mediator;

namespace VroksNet.Application.Connections.ListConnections;

public sealed record ListConnections : IRequest<IReadOnlyList<ConnectionSummary>>;
