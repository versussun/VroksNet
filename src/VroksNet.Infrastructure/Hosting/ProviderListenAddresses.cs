using System.Globalization;

namespace VroksNet.Infrastructure.Hosting;

/// <summary>
/// The addresses ApiService should listen on once provider mode's port is added — the existing
/// ones (ASPNETCORE_URLS, else ASPNETCORE_HTTP(S)_PORTS as the aspnet image sets, else Kestrel's
/// localhost:5000 default) plus the provider port. Pure, so the merge is unit-tested; ApiService's
/// ProviderPortSetup applies the result with UseUrls (which replaces, not extends, the addresses).
/// </summary>
public static class ProviderListenAddresses
{
    /// <exception cref="InvalidOperationException">
    /// The provider port isn't a usable fixed port, or the main surface already listens on it —
    /// either would silently break provider mode or turn the whole API into the mock.
    /// </exception>
    public static IReadOnlyList<string> Merge(string? urls, string? httpPorts, string? httpsPorts, int providerPort)
    {
        if (providerPort is <= 0 or > 65535)
        {
            throw new InvalidOperationException($"Provider:Port must be a fixed port between 1 and 65535 (got {providerPort}).");
        }

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

        if (addresses.Any(address => PortOf(address) == providerPort))
        {
            throw new InvalidOperationException(
                $"Provider:Port {providerPort} is already one of the API's own addresses ({string.Join(";", addresses)}) — every request on it would go to the mock. Pick a different port.");
        }

        // Don't widen exposure: if the main surface is loopback-only (as under AppHost), so is the
        // provider port — the mock has no authentication.
        var host = addresses.All(IsLoopback) ? "localhost" : "*";
        addresses.Add($"http://{host}:{providerPort.ToString(CultureInfo.InvariantCulture)}");
        return addresses;
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
