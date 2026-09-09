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

    public DistributedApplication App { get; private set; } = null!;

    public IBrowser Browser { get; private set; } = null!;

    /// <summary>webfrontend's base address under the AppHost testing proxy — feed this to Playwright as each test's <c>BaseURL</c>.</summary>
    public Uri WebBaseAddress { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        // No TestContext exists yet during fixture construction — CancellationToken.None is the
        // documented exception to the xUnit1051 rule here (see .claude/CLAUDE.md "Testing").
        var cancellationToken = CancellationToken.None;

        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.VroksNet_AppHost>(cancellationToken);
        appHost.Services.ConfigureHttpClientDefaults(clientBuilder =>
        {
            clientBuilder.AddStandardResilienceHandler();
        });

        App = await appHost.BuildAsync(cancellationToken).WaitAsync(StartupTimeout, cancellationToken);
        await App.StartAsync(cancellationToken).WaitAsync(StartupTimeout, cancellationToken);

        await App.ResourceNotifications.WaitForResourceHealthyAsync("webfrontend", cancellationToken).WaitAsync(StartupTimeout, cancellationToken);
        WebBaseAddress = App.GetEndpoint("webfrontend");

        _playwright = await Playwright.CreateAsync();
        // Headless by default (matches CI); set HEADED=1 locally to watch the browser while debugging a test.
        var headless = Environment.GetEnvironmentVariable("HEADED") != "1";
        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = headless });
    }

    public async ValueTask DisposeAsync()
    {
        await Browser.CloseAsync();
        _playwright.Dispose();
        await App.DisposeAsync();
    }
}
