using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using VroksNet.Web;
using VroksNet.Web.Components;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Points at VroksNet.ApiService. In Production, ApiService serves this app itself
// (same origin), so no configured value is needed and the browser's own origin is used.
// In Development, ApiService and Web run as separate processes/ports (see
// appsettings.Development.json), so ApiService needs CORS enabled for Web's origin.
builder.Services.AddHttpClient<WeatherApiClient>(client =>
{
    var apiServiceBaseAddress = builder.Configuration["ApiService:BaseAddress"];
    client.BaseAddress = new Uri(apiServiceBaseAddress ?? builder.HostEnvironment.BaseAddress);
});

await builder.Build().RunAsync();
