using Mediator;

namespace VroksNet.Application.Mocking.SetSpecificationProviderMode;

/// <summary>Turns provider mode on or off for every HTTP operation of one specification. Result is null if no specification with <see cref="SpecificationId"/> exists.</summary>
public sealed record SetSpecificationProviderMode(Guid SpecificationId, bool Enabled) : IRequest<SetSpecificationProviderModeResult?>;
