using Mediator;

namespace VroksNet.Application.Mocking.SetEndpointEnabled;

/// <summary>Turns one operation's mock on or off — a disabled operation answers 404, under "/mock" and on the provider port alike.</summary>
public sealed record SetEndpointEnabled(Guid MockEndpointId, bool Enabled) : IRequest<SetEndpointEnabledResult>;
