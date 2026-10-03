using System.Text.Json.Serialization;
using VroksNet.ApiService.Endpoints;
using VroksNet.Application;
using VroksNet.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    // e.g. SpecificationKind as "OpenApi" instead of 0 — readable in the Admin UI and easier to debug.
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// VroksNet.Web (Blazor WebAssembly) runs as its own dev-server process in Development, on a
// different origin than this API — it needs CORS. In Production it has no separate origin: its
// published output is served as static files by this project (see below), so no CORS is needed.
//
// Allow any localhost/loopback origin (or *.localhost — see below) rather than hardcoding Web's
// launchSettings.json port: that port isn't stable (see .claude/CLAUDE.md on Aspire's random
// dev-time ports). Development-only keeps this safe.
const string WebDevCorsPolicy = "WebDev";
if (builder.Environment.IsDevelopment())
{
    // For anything beyond plain loopback / *.localhost — a real custom hostname mapped in the
    // hosts file, a tunnel domain, etc. — add it here, comma-separated, no code change needed:
    //   Cors__AdditionalDevOrigins=https://my-custom-domain.example:1234,https://other.example
    var additionalDevOrigins = (builder.Configuration["Cors:AdditionalDevOrigins"] ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    builder.Services.AddCors(options =>
    {
        options.AddPolicy(WebDevCorsPolicy, policy =>
        {
            policy.SetIsOriginAllowed(origin =>
                {
                    if (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri))
                    {
                        return false;
                    }

                    // Uri.IsLoopback only matches the literal host "localhost" (or a loopback IP
                    // literal) — it does NOT match "*.localhost" subdomains, even though the whole
                    // .localhost TLD is reserved by RFC 6761 to always resolve to loopback. Aspire's
                    // per-resource dev-domain hostnames (e.g. "webfrontend-vroksnet.dev.localhost")
                    // land exactly in that gap, so they need an explicit suffix check too.
                    if (originUri.IsLoopback || originUri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    return additionalDevOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase);
                })
                .AllowAnyMethod()
                .AllowAnyHeader();
        });
    });
}

// Message broker clients (Aspire-managed connections; see AppHost.cs for the container resources).
// Their health checks are off: the app never uses these clients (brokers are reached through the
// user's own Connections), and the Docker image has no broker at all — with the checks on, /health
// would report the API unhealthy (or throw on the missing connection string) in every deployment.
builder.AddRabbitMQClient("rabbitmq", settings => settings.DisableHealthChecks = true);
builder.AddNatsClient("nats", settings => settings.DisableHealthChecks = true);

var providerPort = builder.ListenOnProviderPort();

var app = builder.Build();

await app.Services.InitializeDatabaseAsync();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

// Must come before anything else serves a request: on the provider port, every path is the mock's.
if (providerPort is { } port)
{
    app.MapProviderPort(port);
}

if (app.Environment.IsDevelopment())
{
    app.UseCors(WebDevCorsPolicy);
}

app.MapGet("/", () => "API service is running.");

app.MapDefaultEndpoints();
app.MapSpecificationEndpoints();
app.MapMockInvocationEndpoints();
app.MapMockEndpointEndpoints();
app.MapConnectionEndpoints();
app.MapTestScenarioEndpoints();
app.MapTestRunEndpoints();
app.MapPublisherEndpoints();
app.MapCallRecordEndpoints();
app.MapSystemEndpoints();

// Serves VroksNet.Web's published Blazor WebAssembly output as static files, with a SPA
// fallback so client-side routes resolve to index.html. In Development, this project's own
// wwwroot is empty (Web runs as its own dev-server process instead) so these are effectively
// dormant; the multi-stage Dockerfile populates wwwroot from VroksNet.Web's publish output for
// Production, where this becomes the only process serving both the API and the Admin UI.
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

app.Run();
