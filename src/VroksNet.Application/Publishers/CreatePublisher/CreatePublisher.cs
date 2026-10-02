using Mediator;

namespace VroksNet.Application.Publishers.CreatePublisher;

/// <summary><see cref="Exchange"/> only applies to a RabbitMQ connection and is dropped otherwise. <see cref="Enabled"/> starts it publishing on its schedule right away.</summary>
public sealed record CreatePublisher(
    string Name,
    Guid SpecificationId,
    Guid MockEndpointId,
    Guid ConnectionId,
    string? PayloadOverride,
    int IntervalSeconds,
    string? Exchange = null,
    bool Enabled = false) : IRequest<Guid>;
