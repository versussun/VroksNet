namespace VroksNet.Web;

/// <summary>The body of a publisher create/update. <see cref="BrokerOptions"/> must be options the connection's type declares (null: the defaults); <see cref="Enabled"/> only on create (start/stop is its own call afterwards).</summary>
public sealed record PublisherForm(
    string Name,
    Guid SpecificationId,
    Guid MockEndpointId,
    Guid ConnectionId,
    string? PayloadOverride,
    int IntervalSeconds,
    Dictionary<string, string>? BrokerOptions,
    bool Enabled);
