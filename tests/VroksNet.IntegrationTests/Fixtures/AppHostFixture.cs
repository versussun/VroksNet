using Aspire.Hosting;

namespace VroksNet.IntegrationTests.Fixtures;

/// <summary>
/// Boots the real Aspire distributed-application graph once — apiservice, webfrontend, and the
/// RabbitMQ/NATS containers apiservice waits on (see AppHost.cs) — and shares it across every
/// test in the "AppHost" collection. Each boot spins up real containers and takes several
/// seconds; per-test boot would make the suite unusably slow. Requires a running container
/// runtime (Docker Desktop or compatible) — see .claude/CLAUDE.md "Testing".
/// </summary>
public sealed class AppHostFixture : IAsyncLifetime
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(2);

    public DistributedApplication App { get; private set; } = null!;

    /// <summary>HttpClient pointed at apiservice's pinned "https" endpoint (see AppHost.cs).</summary>
    public HttpClient ApiServiceClient { get; private set; } = null!;

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

        await App.ResourceNotifications.WaitForResourceHealthyAsync("apiservice", cancellationToken).WaitAsync(StartupTimeout, cancellationToken);

        ApiServiceClient = App.CreateHttpClient("apiservice", "https");
    }

    public async ValueTask DisposeAsync()
    {
        ApiServiceClient.Dispose();
        await App.DisposeAsync();
    }
}
