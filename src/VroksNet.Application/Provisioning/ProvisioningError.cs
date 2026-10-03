namespace VroksNet.Application.Provisioning;

/// <summary>One thing provisioning couldn't do: where (a file, or a manifest entry like "publishers[order-created]") and why. Never contains a connection value.</summary>
public sealed record ProvisioningError(string Source, string Message);
