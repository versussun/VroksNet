namespace VroksNet.Web;

/// <summary>The body of a publisher create/update. <see cref="Exchange"/> only matters for a RabbitMQ connection; <see cref="Enabled"/> only on create (start/stop is its own call afterwards).</summary>
public sealed record PublisherForm(
    string Name,
    Guid SpecificationId,
    Guid MockEndpointId,
    Guid ConnectionId,
    string? PayloadOverride,
    int IntervalSeconds,
    string? Exchange,
    bool Enabled);
