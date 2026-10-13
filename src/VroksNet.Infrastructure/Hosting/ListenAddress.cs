namespace VroksNet.Infrastructure.Hosting;

/// <summary>One address ApiService listens on (see <see cref="ListenAddresses"/>); <see cref="Http2Only"/> for the gRPC port, which needs HTTP/2 without TLS (h2c).</summary>
public sealed record ListenAddress(string Url, bool Http2Only = false);
