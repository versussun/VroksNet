using Aspire.Hosting;

namespace VroksNet.IntegrationTests.Fixtures;

/// <summary>
/// Boots the real Aspire distributed-application graph once — apiservice, webfrontend, and the
/// broker containers named in <paramref name="brokers"/> (AppHost.cs "Brokers") — and shares it
/// across every test in one collection. Each boot spins up real containers and takes several
/// seconds; per-test boot would make the suite unusably slow. Requires a running container
/// runtime (Docker Desktop or compatible) — see .claude/CLAUDE.md "Testing".
/// <para>
/// A base class on purpose: <see cref="AppHostFixture"/> boots the core brokers for the required
/// CI job, and each broker family added later gets its own fixture booting only its broker, in
/// its own collection under <c>Brokers/&lt;Family&gt;</c> (step R6 of docs/broker-adapters-plan.md).
/// </para>
/// </summary>
/// <param name="brokers">AppHost resource names of the brokers to start; empty starts none.</param>
public abstract class AppHostFixtureBase(IReadOnlyList<string> brokers) : IAsyncLifetime
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(2);

    public DistributedApplication App { get; private set; } = null!;

    /// <summary>HttpClient pointed at apiservice's pinned "https" endpoint (see AppHost.cs).</summary>
    public HttpClient ApiServiceClient { get; private set; } = null!;

    /// <summary>
    /// apiservice's plain-http address. Use it as an Http connection's URL whenever ApiService
    /// has to call <em>itself</em> (e.g. a TestScenario run): that send happens server-side
    /// through MessageSender's ordinary HttpClient, which only trusts the https dev certificate
    /// on machines where it's been trusted (`dotnet dev-certs https --trust`) — not on a CI box.
    /// </summary>
    public Uri ApiServiceHttpAddress { get; private set; } = null!;

    /// <summary>
    /// The one browser origin the provider port allows here. AppHost leaves CORS off; the fixture
    /// turns it on for this test graph only, so its preflight handling can be exercised.
    /// </summary>
    public const string AllowedProviderOrigin = "http://allowed.example";

    /// <summary>apiservice's provider-mode port (AppHost.cs "provider" endpoint): the mock at real spec paths, nothing else.</summary>
    public Uri ProviderAddress { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        // No TestContext exists yet during fixture construction — CancellationToken.None is the
        // documented exception to the xUnit1051 rule here (see .claude/CLAUDE.md "Testing").
        var cancellationToken = CancellationToken.None;

        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.VroksNet_AppHost>(
            [$"--Brokers={string.Join(',', brokers)}"], cancellationToken);
        appHost.Services.ConfigureHttpClientDefaults(clientBuilder =>
        {
            clientBuilder.AddStandardResilienceHandler();
        });
        appHost.CreateResourceBuilder<ProjectResource>("apiservice")
            .WithEnvironment("Provider__CorsOrigins", AllowedProviderOrigin);

        App = await appHost.BuildAsync(cancellationToken).WaitAsync(StartupTimeout, cancellationToken);
        await App.StartAsync(cancellationToken).WaitAsync(StartupTimeout, cancellationToken);

        await App.ResourceNotifications.WaitForResourceHealthyAsync("apiservice", cancellationToken).WaitAsync(StartupTimeout, cancellationToken);

        ApiServiceClient = App.CreateHttpClient("apiservice", "https");
        ApiServiceHttpAddress = App.GetEndpoint("apiservice", "http");
        ProviderAddress = App.GetEndpoint("apiservice", "provider");
    }

    public async ValueTask DisposeAsync()
    {
        ApiServiceClient.Dispose();
        await App.DisposeAsync();
    }
}
