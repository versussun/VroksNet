namespace VroksNet.Web;

/// <summary>Whether provider mode's port is configured, its port, and (if the deployment knows it) the base URL a service under test should call.</summary>
public sealed record ProviderInfo(bool Enabled, int? Port, string? PublicUrl);
