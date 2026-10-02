using Mediator;

namespace VroksNet.Application.Publishers.UpdatePublisher;

/// <summary>Result is false if no publisher with <see cref="Id"/> exists. Fields behave as in <see cref="CreatePublisher.CreatePublisher"/>; whether it's enabled is changed separately (<see cref="SetPublisherEnabled.SetPublisherEnabled"/>).</summary>
public sealed record UpdatePublisher(
    Guid Id,
    string Name,
    Guid SpecificationId,
    Guid MockEndpointId,
    Guid ConnectionId,
    string? PayloadOverride,
    int IntervalSeconds,
    string? Exchange = null) : IRequest<bool>;
