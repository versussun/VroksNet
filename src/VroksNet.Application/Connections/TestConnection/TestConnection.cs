using Mediator;
using VroksNet.Application.Abstractions;

namespace VroksNet.Application.Connections.TestConnection;

/// <summary>Result is null if no connection with <see cref="Id"/> exists.</summary>
public sealed record TestConnection(Guid Id) : IRequest<ConnectionTestResult?>;
