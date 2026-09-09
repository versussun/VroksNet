using Mediator;
using VroksNet.Domain.Connections;

namespace VroksNet.Application.Connections.CreateConnection;

public sealed record CreateConnection(string Name, ConnectionServiceType ServiceType, string Value) : IRequest<Guid>;
