var builder = DistributedApplication.CreateBuilder(args);

var rabbitmq = builder.AddRabbitMQ("rabbitmq");
var nats = builder.AddNats("nats");

// Pinned to a fixed port: under AppHost orchestration, Aspire assigns each project resource a
// random port every run rather than honoring its launchSettings.json applicationUrl — confirmed
// by inspecting actual listening ports across several runs, both with and without an explicit
// launch profile. VroksNet.Web (a standalone Blazor WASM app, see below) has no way to discover
// a random port at runtime, so this one has to be stable. Must match the port hardcoded in
// VroksNet.Web/wwwroot/appsettings.Development.json ("ApiService:BaseAddress").
var apiService = builder.AddProject<Projects.VroksNet_ApiService>("apiservice")
    .WithHttpsEndpoint(port: 7352, name: "https")
    .WithHttpHealthCheck("/health")
    .WithReference(rabbitmq)
    .WithReference(nats)
    .WaitFor(rabbitmq)
    .WaitFor(nats);

// VroksNet.Web is a standalone Blazor WebAssembly app (Microsoft.NET.Sdk.BlazorWebAssembly).
// `dotnet run` on it launches its built-in dev server for local hot reload; in Production it
// isn't run as its own process at all — VroksNet.ApiService serves its published output as
// static files instead (see VroksNet.ApiService/Program.cs), so there is no service-discovery
// reference to apiservice here — the dev server talks to ApiService's fixed dev URL configured
// in VroksNet.Web/wwwroot/appsettings.Development.json, with CORS enabled on ApiService for it.
// Web's own dev-server port is left random — ApiService's CORS policy allows any loopback
// origin in Development, so it doesn't need to be predicted.
builder.AddProject<Projects.VroksNet_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WaitFor(apiService);

builder.Build().Run();
