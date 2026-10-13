using System.Net;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using VroksNet.Infrastructure.Hosting;

namespace VroksNet.ApiService.Endpoints;

/// <summary>
/// The extra ports next to the API's own: provider mode's (<c>Provider:Port</c>, see
/// <see cref="ProviderPortSetup"/>) and the gRPC mock's (<c>Grpc:Port</c>, see <see cref="GrpcPortSetup"/>).
/// The gRPC port must be HTTP/2 only (h2c), which an address passed to UseUrls can't say, so when
/// either port is set every address is bound explicitly — and since an explicit binding replaces
/// UseUrls/ASPNETCORE_URLS, the list (<see cref="ListenAddresses.Merge"/>) holds the API's own too.
/// </summary>
public static class ListenSetup
{
    public const string GrpcPortConfigKey = "Grpc:Port";

    public static (int? ProviderPort, int? GrpcPort) ListenOnMockPorts(this WebApplicationBuilder builder)
    {
        var providerPort = builder.Configuration.GetValue<int?>(ProviderPortSetup.PortConfigKey);
        var grpcPort = builder.Configuration.GetValue<int?>(GrpcPortConfigKey);
        if (providerPort is null && grpcPort is null)
        {
            return (null, null);
        }

        // Kestrel would ignore these bindings for its configured endpoints, leaving a mock silently dead.
        if (builder.Configuration.GetSection("Kestrel:Endpoints").GetChildren().Any())
        {
            throw new InvalidOperationException(
                "Provider:Port and Grpc:Port can't be combined with Kestrel:Endpoints configuration (Kestrel would ignore the added addresses). Configure them as Kestrel endpoints instead, or drop Kestrel:Endpoints.");
        }

        var addresses = ListenAddresses.Merge(
            builder.Configuration["urls"], builder.Configuration["http_ports"], builder.Configuration["https_ports"], providerPort, grpcPort);
        // They're all bound below; left in place, Kestrel would warn that it's overriding them.
        builder.WebHost.UseSetting(WebHostDefaults.ServerUrlsKey, string.Empty);
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            foreach (var address in addresses)
            {
                Bind(kestrel, address);
            }
        });

        if (providerPort is not null)
        {
            ProviderPortSetup.AddProviderCors(builder);
        }

        return (providerPort, grpcPort);
    }

    /// <summary>What UseUrls would do with the address: any host for "*"/"+"/a name, loopback for "localhost", else that IP; TLS for https with the default certificate.</summary>
    private static void Bind(KestrelServerOptions kestrel, ListenAddress address)
    {
        var uri = new Uri(address.Url.Replace("://*", "://any-host", StringComparison.Ordinal).Replace("://+", "://any-host", StringComparison.Ordinal));
        void Configure(ListenOptions options)
        {
            if (address.Http2Only)
            {
                options.Protocols = HttpProtocols.Http2;
            }

            if (uri.Scheme == Uri.UriSchemeHttps)
            {
                options.UseHttps();
            }
        }

        if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            kestrel.ListenLocalhost(uri.Port, Configure);
        }
        else if (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip) && !ip.Equals(IPAddress.Any) && !ip.Equals(IPAddress.IPv6Any))
        {
            kestrel.Listen(ip, uri.Port, Configure);
        }
        else
        {
            kestrel.ListenAnyIP(uri.Port, Configure);
        }
    }
}
