using VroksNet.IntegrationTests.Fixtures;

namespace VroksNet.IntegrationTests.Brokers.Redis;

/// <summary>The graph with only the Redis server (AppHost.cs "redis") — the N3 broker family.</summary>
public sealed class RedisAppHostFixture() : AppHostFixtureBase(["redis"])
{
    /// <summary>
    /// The server as a Redis connection value: exactly the connection string Aspire's
    /// <c>WithReference</c> hands out (<c>host:port,password=…</c>), which is what the Aspire
    /// package passes through <c>valueFrom</c>.
    /// </summary>
    public async Task<string> ConnectionValueAsync(CancellationToken cancellationToken)
        => await App.GetConnectionStringAsync("redis", cancellationToken)
            ?? throw new InvalidOperationException("The redis resource has no connection string.");
}
