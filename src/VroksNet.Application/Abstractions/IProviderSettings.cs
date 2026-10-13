namespace VroksNet.Application.Abstractions;

/// <summary>
/// How provider mode's port is configured (<c>Provider:Port</c>, <c>Provider:PublicUrl</c>,
/// <c>Provider:CorsOrigins</c>) — for
/// display only, like <see cref="IStorageStatusProvider"/>. <see cref="PublicUrl"/> is the address a
/// service under test should use, when the deployment knows it (AppHost sets it; behind Docker's
/// port mapping only the operator does).
/// </summary>
public interface IProviderSettings
{
    int? Port { get; }

    string? PublicUrl { get; }

    /// <summary>The browser origins allowed to call the provider port (<c>["*"]</c> for any) — empty when CORS is off there.</summary>
    IReadOnlyList<string> CorsOrigins { get; }

    /// <summary>The gRPC mock's port (<c>Grpc:Port</c>, ADR 0004); null when it's off.</summary>
    int? GrpcPort { get; }

    /// <summary>The gRPC mock's address as clients see it (<c>Grpc:PublicUrl</c>), when the deployment knows it.</summary>
    string? GrpcPublicUrl { get; }
}
