using System.Globalization;

namespace VroksNet.Infrastructure.Hosting;

/// <summary>
/// The addresses ApiService listens on once the extra ports are added — the existing ones
/// (ASPNETCORE_URLS, else ASPNETCORE_HTTP(S)_PORTS as the aspnet image sets, else Kestrel's
/// localhost:5000 default) plus provider mode's port and the gRPC mock's (ADR 0004), which must be
/// HTTP/2 only: without TLS, Kestrel takes HTTP/2 by prior knowledge (h2c) only on such an endpoint.
/// Pure, so the merge is unit-tested; ApiService's ListenSetup binds the result explicitly (an
/// explicit Kestrel binding replaces UseUrls, so every address has to be in the list).
/// </summary>
public static class ListenAddresses
{
    /// <exception cref="InvalidOperationException">
    /// A port isn't a usable fixed port, or two surfaces would share one — either would silently
    /// break a mock or turn the whole API into it.
    /// </exception>
    public static IReadOnlyList<ListenAddress> Merge(string? urls, string? httpPorts, string? httpsPorts, int? providerPort, int? grpcPort)
    {
        var addresses = Split(urls).ToList();
        if (addresses.Count == 0)
        {
            addresses.AddRange(Split(httpPorts).Select(port => $"http://*:{port}"));
            addresses.AddRange(Split(httpsPorts).Select(port => $"https://*:{port}"));
        }

        if (addresses.Count == 0)
        {
            addresses.Add("http://localhost:5000"); // Kestrel's own default when nothing is configured
        }

        // Don't widen exposure: if the main surface is loopback-only (as under AppHost), so are the
        // added ports — the mocks have no authentication.
        var host = addresses.All(IsLoopback) ? "localhost" : "*";
        var merged = addresses.Select(address => new ListenAddress(address)).ToList();
        Add(merged, "Provider:Port", providerPort, host, http2Only: false);
        Add(merged, "Grpc:Port", grpcPort, host, http2Only: true);
        return merged;
    }

    private static void Add(List<ListenAddress> addresses, string setting, int? port, string host, bool http2Only)
    {
        if (port is not { } value)
        {
            return;
        }

        if (value is <= 0 or > 65535)
        {
            throw new InvalidOperationException($"{setting} must be a fixed port between 1 and 65535 (got {value}).");
        }

        if (addresses.Any(address => PortOf(address.Url) == value))
        {
            throw new InvalidOperationException(
                $"{setting} {value} is already one of the server's addresses ({string.Join(";", addresses.Select(address => address.Url))}) — every request on it would go to the wrong place. Pick a different port.");
        }

        addresses.Add(new ListenAddress($"http://{host}:{value.ToString(CultureInfo.InvariantCulture)}", http2Only));
    }
    private static IEnumerable<string> Split(string? value)
        => (value ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>"*" and "+" (any host) are rewritten to a parseable placeholder; they never count as loopback.</summary>
    private static Uri? Parse(string address)
        => Uri.TryCreate(address.Replace("://*", "://any-host", StringComparison.Ordinal).Replace("://+", "://any-host", StringComparison.Ordinal), UriKind.Absolute, out var uri)
            ? uri
            : null;

    private static int? PortOf(string address) => Parse(address)?.Port;

    private static bool IsLoopback(string address)
        => Parse(address) is { } uri && (uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase));
}
