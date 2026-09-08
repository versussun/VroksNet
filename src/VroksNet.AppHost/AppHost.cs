var builder = DistributedApplication.CreateBuilder(args);

var rabbitmq = builder.AddRabbitMQ("rabbitmq");
var nats = builder.AddNats("nats");

var apiService = builder.AddProject<Projects.VroksNet_ApiService>("apiservice")
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
builder.AddProject<Projects.VroksNet_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WaitFor(apiService);

builder.Build().Run();
