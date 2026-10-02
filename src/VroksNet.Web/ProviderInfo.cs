namespace VroksNet.Web;

/// <summary>Whether provider mode's port is configured, its port, (if the deployment knows it) the base URL a service under test should call, and the browser origins allowed to call it (empty: CORS off).</summary>
public sealed record ProviderInfo(bool Enabled, int? Port, string? PublicUrl, string[]? CorsOrigins);
