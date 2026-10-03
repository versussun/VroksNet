namespace VroksNet.Application.Provisioning;

/// <summary>A Publisher to create or bring in line by <see cref="Name"/>; the spec by title, the operation by key, the connection by name.</summary>
public sealed record ManifestPublisher(
    string Name,
    string Specification,
    string Operation,
    string Connection,
    int IntervalSeconds,
    string? Exchange,
    string? PayloadOverride,
    bool Enabled);
