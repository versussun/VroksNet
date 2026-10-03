namespace VroksNet.Application.Provisioning;

/// <summary>A Publisher to create or bring in line by <see cref="Name"/>; the spec by title, the operation by key, the connection by name. <see cref="Exchange"/> is the deprecated spelling of <c>BrokerOptions["exchange"]</c> (ADR 0003).</summary>
public sealed record ManifestPublisher(
    string Name,
    string Specification,
    string Operation,
    string Connection,
    int IntervalSeconds,
    string? Exchange,
    string? PayloadOverride,
    bool Enabled,
    IReadOnlyDictionary<string, string?>? BrokerOptions = null);
