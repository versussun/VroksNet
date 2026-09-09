using Mediator;

namespace VroksNet.Application.Connections.DeleteConnection;

/// <summary>Result is false if no connection with <see cref="Id"/> exists.</summary>
public sealed record DeleteConnection(Guid Id) : IRequest<bool>;
