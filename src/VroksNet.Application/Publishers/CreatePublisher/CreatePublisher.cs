using Mediator;

namespace VroksNet.Application.Publishers.CreatePublisher;

/// <summary>
/// <see cref="BrokerOptions"/> must be options the connection's type declares (ADR 0003); <see cref="Exchange"/> is the deprecated
/// spelling of <c>BrokerOptions["exchange"]</c>, dropped for a type without that option. <see cref="Enabled"/> starts it publishing on its schedule right away.
/// </summary>
public sealed record CreatePublisher(
    string Name,
    Guid SpecificationId,
    Guid MockEndpointId,
    Guid ConnectionId,
    string? PayloadOverride,
    int IntervalSeconds,
    string? Exchange = null,
    bool Enabled = false,
    IReadOnlyDictionary<string, string?>? BrokerOptions = null) : IRequest<Guid>;
