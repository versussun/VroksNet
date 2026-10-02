namespace VroksNet.Application.System.GetProviderInfo;

/// <summary>Whether provider mode's port is configured at all, its port, and (if known) the base URL a service under test should call.</summary>
public sealed record ProviderInfo(bool Enabled, int? Port, string? PublicUrl);
