namespace VroksNet.E2ETests.Fixtures;

/// <summary>
/// Boots the real Aspire distributed-application graph once — apiservice, webfrontend, and the
/// RabbitMQ/NATS containers apiservice waits on (see AppHost.cs) — and launches one shared
/// Playwright browser, both reused across every test in the "AppHost" collection: booting real
/// containers and launching a browser are each too slow to redo per test. Requires a running
/// container runtime (Docker Desktop or compatible) and Playwright's browser binaries installed
/// (`playwright install`) — see .claude/CLAUDE.md "Testing".
/// </summary>
public sealed class AppHostFixture : IAsyncLifetime
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(2);

    private IPlaywright _playwright = null!;
    private string? _webDevAppSettingsPath;
    private string? _originalWebDevAppSettings;

    public DistributedApplication App { get; private set; } = null!;

    public IBrowser Browser { get; private set; } = null!;

    /// <summary>webfrontend's base address under the AppHost testing proxy — feed this to Playwright as each test's <c>BaseURL</c>.</summary>
    public Uri WebBaseAddress { get; private set; } = null!;

    /// <summary>
    /// apiservice's plain-http address. Use it as an Http connection's URL whenever ApiService
    /// has to call <em>itself</em> (e.g. a TestScenario run): that send happens server-side
    /// through MessageSender's ordinary HttpClient, which only trusts the https dev certificate
    /// on machines where it's been trusted (`dotnet dev-certs https --trust`) — not on a CI box.
    /// </summary>
    public Uri ApiServiceHttpAddress { get; private set; } = null!;

    /// <summary>
    /// A connection the test graph declares through <c>Provisioning__Connections__0__*</c>
    /// (test graph only; AppHost provisions nothing), so the UI has a provisioned object to badge.
    /// </summary>
    public const string ProvisionedConnectionName = "e2e-provisioned";

    public async ValueTask InitializeAsync()
    {
        // No TestContext exists yet during fixture construction — CancellationToken.None is the
        // documented exception to the xUnit1051 rule here (see .claude/CLAUDE.md "Testing").
        var cancellationToken = CancellationToken.None;

        // The core brokers only: the browser tests don't need a broker added later, and each one
        // would add a container to every run (see AppHost.cs "Brokers").
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.VroksNet_AppHost>(
            ["--Brokers=rabbitmq,nats,kafka"], cancellationToken);
        appHost.Services.ConfigureHttpClientDefaults(clientBuilder =>
        {
            clientBuilder.AddStandardResilienceHandler();
        });
        appHost.CreateResourceBuilder<ProjectResource>("apiservice")
            .WithEnvironment("Provisioning__Connections__0__Name", ProvisionedConnectionName)
            .WithEnvironment("Provisioning__Connections__0__Type", "Http")
            .WithEnvironment("Provisioning__Connections__0__Value", "http://provisioned.invalid");

        App = await appHost.BuildAsync(cancellationToken).WaitAsync(StartupTimeout, cancellationToken);
        await App.StartAsync(cancellationToken).WaitAsync(StartupTimeout, cancellationToken);

        // apiservice's port pin (AppHost.cs: WithHttpsEndpoint(port: 7352, ...)) is a DCP-proxy
        // feature of a real `dotnet run` AppHost process — confirmed directly
        // (Get-NetTCPConnection during a live run) that DistributedApplicationTestingBuilder does
        // NOT bind that literal port at all; apiservice gets a genuine random port instead, same
        // as every other resource under this builder. Web/wwwroot/appsettings.Development.json
        // hardcodes 7352, so the WASM app — which reads that file itself via its own fetch(),
        // unlike .NET clients such as CreateHttpClient below, which resolve the real endpoint
        // through Aspire directly — tries to reach a port nothing is listening on, and every
        // fetch() the app makes fails with net::ERR_CONNECTION_REFUSED (confirmed directly via
        // Page.RequestFailed).
        //
        // Can't fix this by pointing the WASM app at a different appsettings.{Environment}.json
        // at boot: standalone Blazor WebAssembly's environment is now (.NET 10) a build-time-only
        // MSBuild property (WasmApplicationEnvironmentName) baked into the already-built output —
        // ASPNETCORE_ENVIRONMENT set on the dev-server process (which controlled this in .NET
        // 8/9) is no longer read at runtime, confirmed by testing that approach directly and
        // seeing it have zero effect. So instead: overwrite the actual file the dev server serves
        // with this run's real apiservice endpoint, restoring the original content in
        // DisposeAsync. Every test in the collection shares this one instance/one file, so this
        // only needs to happen once, here.
        var apiServiceEndpoint = App.GetEndpoint("apiservice", "https");
        ApiServiceHttpAddress = App.GetEndpoint("apiservice", "http");
        _webDevAppSettingsPath = Path.Combine(appHost.AppHostDirectory, "..", "VroksNet.Web", "wwwroot", "appsettings.Development.json");
        _originalWebDevAppSettings = await File.ReadAllTextAsync(_webDevAppSettingsPath, cancellationToken);
        var patchedAppSettings = _originalWebDevAppSettings.Replace("https://localhost:7352", apiServiceEndpoint.ToString().TrimEnd('/'));
        await File.WriteAllTextAsync(_webDevAppSettingsPath, patchedAppSettings, cancellationToken);

        await App.ResourceNotifications.WaitForResourceHealthyAsync("webfrontend", cancellationToken).WaitAsync(StartupTimeout, cancellationToken);
        WebBaseAddress = App.GetEndpoint("webfrontend");

        _playwright = await Playwright.CreateAsync();
        // Headless by default (matches CI); set HEADED=1 locally to watch the browser while debugging a test.
        var headless = Environment.GetEnvironmentVariable("HEADED") != "1";
        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = headless });

        // Playwright's Expect(...) default (5s) is too tight here, confirmed by direct testing —
        // a fresh IBrowserContext's first page load pays Blazor WASM's full interpreted-mode
        // startup cost, and its first outbound HTTP call goes through the standard resilience
        // handler's retry/backoff (see ServiceDefaults) before any connection is warm. Both are
        // one-time-per-context costs, not a sign anything's actually hung.
        Assertions.SetDefaultExpectTimeout(15_000);
    }

    public async ValueTask DisposeAsync()
    {
        // InitializeAsync may have stopped part-way (webfrontend outlasting the startup timeout,
        // say): dispose only what exists, so its failure is the one reported. Each step runs even
        // if the one before threw (a crashed browser, a HEADED window closed by hand): the
        // containers still stop, and the patched appsettings file is put back, or it stays
        // modified in the working tree.
        try
        {
            try
            {
                if (Browser is not null)
                {
                    await Browser.CloseAsync();
                }
            }
            finally
            {
                try
                {
                    _playwright?.Dispose();
                }
                finally
                {
                    if (App is not null)
                    {
                        await App.DisposeAsync();
                    }
                }
            }
        }
        finally
        {
            if (_webDevAppSettingsPath is not null && _originalWebDevAppSettings is not null)
            {
                await File.WriteAllTextAsync(_webDevAppSettingsPath, _originalWebDevAppSettings, CancellationToken.None);
            }
        }
    }
}
