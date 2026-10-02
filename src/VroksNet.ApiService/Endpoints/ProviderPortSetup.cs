using Mediator;
using VroksNet.Infrastructure.Hosting;

namespace VroksNet.ApiService.Endpoints;

/// <summary>
/// Provider mode ("Тип 3" in docs/contract-testing-plan.md): a second port on which every request
/// is answered by the mock at its real path — "GET /orders" rather than "GET /mock/orders" — so a
/// real service only needs its host:port pointed here. Being a separate port is what keeps a spec's
/// paths from ever colliding with ApiService's own routes (/api, /health, the Admin UI and its SPA
/// fallback): nothing but the mock is reachable on it. Off unless <c>Provider:Port</c> is set
/// (env <c>Provider__Port</c>); AppHost and the Dockerfile both set it.
/// </summary>
public static class ProviderPortSetup
{
    public const string PortConfigKey = "Provider:Port";

    /// <summary>
    /// Adds the provider port to the addresses the server already listens on (see
    /// <see cref="ProviderListenAddresses.Merge"/>, which also refuses a port that would collide
    /// with them). Kestrel ignores UseUrls when <c>Kestrel:Endpoints</c> is configured, which would
    /// leave provider mode silently dead — so that combination fails at startup instead.
    /// </summary>
    public static int? ListenOnProviderPort(this WebApplicationBuilder builder)
    {
        if (builder.Configuration.GetValue<int?>(PortConfigKey) is not { } port)
        {
            return null;
        }

        if (builder.Configuration.GetSection("Kestrel:Endpoints").GetChildren().Any())
        {
            throw new InvalidOperationException(
                "Provider:Port can't be combined with Kestrel:Endpoints configuration (Kestrel would ignore the added address). Configure the provider port as one of the Kestrel endpoints' ports instead, or drop Kestrel:Endpoints.");
        }

        var addresses = ProviderListenAddresses.Merge(
            builder.Configuration["urls"], builder.Configuration["http_ports"], builder.Configuration["https_ports"], port);
        builder.WebHost.UseUrls([.. addresses]);
        return port;
    }

    /// <summary>
    /// Branches every request on <paramref name="port"/> straight into the mock, ahead of static
    /// files, the SPA fallback and every mapped endpoint (endpoint *matching* still runs first, but
    /// the matched endpoint never executes on this branch).
    /// </summary>
    public static void MapProviderPort(this WebApplication app, int port)
    {
        app.MapWhen(context => context.Connection.LocalPort == port, provider => provider.Run(async context =>
        {
            var mediator = context.RequestServices.GetRequiredService<IMediator>();
            var result = await MockInvocationEndpoints.InvokeAsync(context, mediator, context.Request.Path.Value ?? "/", providerMode: true, context.RequestAborted);
            await result.ExecuteAsync(context);
        }));
    }
}
