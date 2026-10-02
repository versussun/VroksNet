using Mediator;
using VroksNet.Infrastructure.Hosting;

namespace VroksNet.ApiService.Endpoints;

/// <summary>
/// Provider mode ("Type 3" in docs/contract-testing-plan.md): a second port on which every request
/// is answered by the mock at its real path — "GET /orders" rather than "GET /mock/orders" — so a
/// real service only needs its host:port pointed here. Being a separate port is what keeps a spec's
/// paths from ever colliding with ApiService's own routes (/api, /health, the Admin UI and its SPA
/// fallback): nothing but the mock is reachable on it. Off unless <c>Provider:Port</c> is set
/// (env <c>Provider__Port</c>); AppHost and the Dockerfile both set it. CORS on it is off unless
/// <c>Provider:CorsOrigins</c> lists the browser origins allowed (see <see cref="ProviderSettings.CorsOriginsFrom"/>).
/// </summary>
public static class ProviderPortSetup
{
    public const string PortConfigKey = "Provider:Port";

    private const string CorsPolicy = "Provider";

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
        AddProviderCors(builder);
        return port;
    }

    /// <summary>
    /// Its own policy, separate from the Admin UI's dev one: any method and header, no credentials
    /// (the mock isn't about auth). Registered only when origins are configured.
    /// </summary>
    private static void AddProviderCors(WebApplicationBuilder builder)
    {
        var origins = ProviderSettings.CorsOriginsFrom(builder.Configuration);
        if (origins.Count == 0)
        {
            return;
        }

        builder.Services.AddCors(options => options.AddPolicy(CorsPolicy, policy =>
        {
            if (origins is ["*"])
            {
                policy.AllowAnyOrigin();
            }
            else
            {
                policy.WithOrigins([.. origins]);
            }

            policy.AllowAnyMethod().AllowAnyHeader();
        }));
    }

    /// <summary>
    /// Branches every request on <paramref name="port"/> straight into the mock, ahead of static
    /// files, the SPA fallback and every mapped endpoint (endpoint *matching* still runs first, but
    /// the matched endpoint never executes on this branch).
    /// </summary>
    /// <remarks>
    /// With CORS configured, the CORS middleware answers a preflight itself — it never reaches the
    /// mock or the call history — and adds the allow headers to the mock's ordinary responses.
    /// </remarks>
    public static void MapProviderPort(this WebApplication app, int port)
    {
        var corsEnabled = ProviderSettings.CorsOriginsFrom(app.Configuration).Count > 0;
        app.MapWhen(context => context.Connection.LocalPort == port, provider =>
        {
            if (corsEnabled)
            {
                // Endpoint matching already ran, so a real path that happens to equal one of the
                // API's routes carries that endpoint — and CorsMiddleware prefers an endpoint's own
                // CORS metadata over the named policy. Nothing on this branch is an API endpoint.
                provider.Use((context, next) =>
                {
                    context.SetEndpoint(null);
                    return next(context);
                });
                provider.UseCors(CorsPolicy);
            }

            provider.Run(async context =>
            {
                var mediator = context.RequestServices.GetRequiredService<IMediator>();
                var result = await MockInvocationEndpoints.InvokeAsync(context, mediator, context.Request.Path.Value ?? "/", providerMode: true, context.RequestAborted);
                await result.ExecuteAsync(context);
            });
        });
    }
}
