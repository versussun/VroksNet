using VroksNet.Domain.Connections;
using VroksNet.Infrastructure.Connections;
using VroksNet.UnitTests.TestDoubles;

namespace VroksNet.UnitTests.Connections;

/// <summary>
/// Exercises the real <see cref="ConnectionTester"/> (not a fake) — no Docker/AppHost needed,
/// unlike VroksNet.IntegrationTests' end-to-end coverage of the same feature: these only ever
/// connect to definitely-closed local ports or a domain RFC 2606 reserves to never resolve, so
/// they exercise the failure path fast and deterministically, offline.
/// </summary>
public class ConnectionTesterTests
{
    private static readonly ConnectionTester Tester = new(BrokerAdapters.Registry());

    [Fact]
    public async Task TestAsync_Http_InvalidUrl_FailsWithoutAttemptingAnyRequest()
    {
        var result = await Tester.TestAsync(Connection("Http", "not a url"), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal("Not a valid absolute URL.", result.Message);
    }

    [Fact]
    public async Task TestAsync_Http_UnresolvableHost_Fails()
    {
        var result = await Tester.TestAsync(Connection("Http", "https://this-host-does-not-exist.invalid"), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public async Task TestAsync_RabbitMq_InvalidConnectionString_FailsWithoutAttemptingAnyRequest()
    {
        var result = await Tester.TestAsync(Connection("RabbitMq", "not a uri"), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal("Not a valid amqp(s):// connection string.", result.Message);
    }

    [Fact]
    public async Task TestAsync_RabbitMq_ClosedPort_Fails()
    {
        // Port 1 ("tcpmux") is reserved and essentially never has anything listening on loopback.
        var result = await Tester.TestAsync(Connection("RabbitMq", "amqp://guest:guest@127.0.0.1:1"), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public async Task TestAsync_Nats_ClosedPort_Fails()
    {
        var result = await Tester.TestAsync(Connection("Nats", "nats://127.0.0.1:1"), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Theory]
    [InlineData("security.protocol=SASL_SSL;sasl.username=u")]
    [InlineData("bootstrap.servers=host:9092;not-a-pair")]
    [InlineData("   ")]
    public async Task TestAsync_Kafka_InvalidConnectionString_FailsWithoutAttemptingAnyRequest(string value)
    {
        var result = await Tester.TestAsync(Connection("Kafka", value), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.StartsWith("Not a valid Kafka connection string", result.Message);
    }

    [Theory]
    [InlineData("127.0.0.1:1")]
    [InlineData("bootstrap.servers=127.0.0.1:1;client.id=vroksnet-test")]
    public async Task TestAsync_Kafka_ClosedPort_Fails(string value)
    {
        var result = await Tester.TestAsync(Connection("Kafka", value), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    private static Connection Connection(string serviceType, string value) => new()
    {
        Id = Guid.NewGuid(),
        Name = "test",
        ServiceType = Enum.Parse<ConnectionServiceType>(serviceType),
        Value = value
    };
}
