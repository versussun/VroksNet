namespace VroksNet.Domain.Connections;

/// <summary>
/// A named connection to an external system — a plain URL or a connection string depending on
/// <see cref="ServiceType"/>. Admin-managed config for systems later features (e.g. the AsyncAPI
/// publish worker) will connect to; distinct from the dev-time Aspire-wired broker connections in
/// AppHost.cs, which stay as they are.
/// </summary>
public sealed class Connection
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public ConnectionServiceType ServiceType { get; set; }

    /// <summary>The URL (for <see cref="ConnectionServiceType.Http"/>) or connection string (for the brokers) — stored as-is, masked only in the UI.</summary>
    public string Value { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// When provisioning (ADR 0001) last brought this connection in line with the manifest or the specs
    /// folder; null for one created in the UI or the API. A provisioned object is overwritten on
    /// every start — UI edits to it don't stick.
    /// </summary>
    public DateTimeOffset? ProvisionedAt { get; set; }
}
