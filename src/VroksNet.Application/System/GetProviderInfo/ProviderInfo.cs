namespace VroksNet.Application.System.GetProviderInfo;

/// <summary>
/// Whether provider mode's port is configured at all, its port, (if known) the base URL a service
/// under test should call, and which browser origins may call it — empty means CORS is off, so a
/// browser front end pointed there fails its preflight. <see cref="GrpcPort"/>/<see cref="GrpcPublicUrl"/>
/// are the gRPC mock's (ADR 0004): null port when it's off.
/// </summary>
public sealed record ProviderInfo(bool Enabled, int? Port, string? PublicUrl, IReadOnlyList<string> CorsOrigins, int? GrpcPort = null, string? GrpcPublicUrl = null);
