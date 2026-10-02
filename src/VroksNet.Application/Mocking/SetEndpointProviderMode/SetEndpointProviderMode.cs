using Mediator;

namespace VroksNet.Application.Mocking.SetEndpointProviderMode;

/// <summary>Turns provider mode (serving at the real path on the provider port) on or off for one operation.</summary>
public sealed record SetEndpointProviderMode(Guid MockEndpointId, bool Enabled) : IRequest<SetEndpointProviderModeResult>;
