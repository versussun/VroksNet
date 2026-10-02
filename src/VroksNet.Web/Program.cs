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
var apiServiceBaseAddress = new Uri(builder.Configuration["ApiService:BaseAddress"] ?? builder.HostEnvironment.BaseAddress);

builder.Services.AddHttpClient<SpecificationApiClient>(client => client.BaseAddress = apiServiceBaseAddress);
builder.Services.AddHttpClient<ConnectionApiClient>(client => client.BaseAddress = apiServiceBaseAddress);
builder.Services.AddHttpClient<TestScenarioApiClient>(client => client.BaseAddress = apiServiceBaseAddress);
builder.Services.AddHttpClient<CallRecordApiClient>(client => client.BaseAddress = apiServiceBaseAddress);
builder.Services.AddHttpClient<SystemApiClient>(client => client.BaseAddress = apiServiceBaseAddress);

await builder.Build().RunAsync();
