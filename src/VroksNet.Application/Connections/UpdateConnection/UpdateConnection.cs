using Mediator;
using VroksNet.Domain.Connections;

namespace VroksNet.Application.Connections.UpdateConnection;

/// <summary>Result is false if no connection with <see cref="Id"/> exists.</summary>
public sealed record UpdateConnection(Guid Id, string Name, ConnectionServiceType ServiceType, string Value) : IRequest<bool>;
