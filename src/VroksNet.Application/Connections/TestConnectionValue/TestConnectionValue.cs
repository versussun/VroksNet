using Mediator;
using VroksNet.Application.Abstractions;
using VroksNet.Domain.Connections;

namespace VroksNet.Application.Connections.TestConnectionValue;

/// <summary>
/// Tests a URL/connection string directly, without it being a saved <see cref="Connection"/> yet
/// — for the "Test" button on the Add/Edit Connection form. The sibling
/// VroksNet.Application.Connections.TestConnection.TestConnection request tests an already-saved
/// one by id instead.
/// </summary>
public sealed record TestConnectionValue(ConnectionServiceType ServiceType, string Value) : IRequest<ConnectionTestResult>;
