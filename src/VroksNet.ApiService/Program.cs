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
// Allow any localhost/loopback origin rather than hardcoding Web's launchSettings.json port:
// that port isn't stable — it depends on which launch profile is used, whether Aspire's AppHost
// assigns it dynamically, IDE debug-launch settings, etc. Loopback-only + Development-only keeps
// this safe (never active in Production, never allows a non-local origin).
const string WebDevCorsPolicy = "WebDev";
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCors(options =>
    {
        options.AddPolicy(WebDevCorsPolicy, policy =>
        {
            policy.SetIsOriginAllowed(origin =>
                    Uri.TryCreate(origin, UriKind.Absolute, out var originUri) && originUri.IsLoopback)
                .AllowAnyMethod()
                .AllowAnyHeader();
        });
    });
}

// Message broker clients (Aspire-managed connections; see AppHost.cs for the container resources).
builder.AddRabbitMQClient("rabbitmq");
builder.AddNatsClient("nats");

var app = builder.Build();

await app.Services.InitializeDatabaseAsync();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseCors(WebDevCorsPolicy);
}

string[] summaries = ["Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"];

app.MapGet("/", () => "API service is running. Navigate to /weatherforecast to see sample data.");

app.MapGet("/weatherforecast", () =>
{
    var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");

app.MapDefaultEndpoints();
app.MapSpecificationEndpoints();

// Serves VroksNet.Web's published Blazor WebAssembly output as static files, with a SPA
// fallback so client-side routes resolve to index.html. In Development, this project's own
// wwwroot is empty (Web runs as its own dev-server process instead) so these are effectively
// dormant; the multi-stage Dockerfile populates wwwroot from VroksNet.Web's publish output for
// Production, where this becomes the only process serving both the API and the Admin UI.
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

app.Run();

sealed record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
