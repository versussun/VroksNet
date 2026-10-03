using VroksNet.Domain.Connections;
using VroksNet.Infrastructure.Connections;
using VroksNet.UnitTests.TestDoubles;

namespace VroksNet.UnitTests.TestScenarios;

/// <summary>
/// The real <see cref="MessageListener"/> against unreachable or mismatched targets — the
/// failure paths, fast and offline, like <see cref="MessageSenderTests"/>. Receiving for real is
/// covered against the AppHost's brokers in VroksNet.IntegrationTests.
/// </summary>
public class MessageListenerTests
{
    private static readonly MessageListener Listener = new(BrokerAdapters.Registry());

    [Fact]
    public async Task ListenAsync_HttpOperation_FailsWithoutConnecting()
    {
        var result = await Listener.ListenAsync(Connection(ConnectionServiceType.RabbitMq, "amqp://127.0.0.1:1"), "GET /pets", TimeSpan.FromSeconds(1), "amq.topic", TestContext.Current.CancellationToken);

        Assert.False(result.Received);
        Assert.Contains("AsyncAPI-shaped", result.Message);
    }

    [Fact]
    public async Task ListenAsync_HttpConnection_Fails()
    {
        var result = await Listener.ListenAsync(Connection(ConnectionServiceType.Http, "https://api.example.com"), "orders.created:send", TimeSpan.FromSeconds(1), "amq.topic", TestContext.Current.CancellationToken);

        Assert.False(result.Received);
        Assert.Contains("Listen needs a broker connection", result.Message);
    }

    [Theory]
    [InlineData(ConnectionServiceType.RabbitMq, "amqp://guest:guest@127.0.0.1:1")]
    [InlineData(ConnectionServiceType.Nats, "nats://127.0.0.1:1")]
    [InlineData(ConnectionServiceType.Kafka, "127.0.0.1:1")]
    public async Task ListenAsync_UnreachableBroker_FailsWithoutThrowing(ConnectionServiceType serviceType, string value)
    {
        var result = await Listener.ListenAsync(Connection(serviceType, value), "orders.created:send", TimeSpan.FromSeconds(1), "amq.topic", TestContext.Current.CancellationToken);

        Assert.False(result.Received);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public async Task ListenAsync_Kafka_InvalidConnectionString_FailsWithoutConnecting()
    {
        var result = await Listener.ListenAsync(Connection(ConnectionServiceType.Kafka, "client.id=no-servers"), "orders.created:send", TimeSpan.FromSeconds(1), "amq.topic", TestContext.Current.CancellationToken);

        Assert.False(result.Received);
        Assert.StartsWith("Not a valid Kafka connection string", result.Message);
    }

    [Theory]
    [InlineData(ConnectionServiceType.RabbitMq, "amqp://guest:guest@127.0.0.1:1")]
    [InlineData(ConnectionServiceType.Nats, "nats://127.0.0.1:1")]
    public async Task ListenAsync_SlashSeparatedParameterOnADotWildcardBroker_FailsWithoutConnecting(ConnectionServiceType serviceType, string value)
    {
        var result = await Listener.ListenAsync(Connection(serviceType, value), "user/{userId}/signedup:send", TimeSpan.FromSeconds(1), "amq.topic", TestContext.Current.CancellationToken);

        Assert.False(result.Received);
        Assert.Contains("\"/\"-separated segments", result.Message);
    }

    [Fact]
    public async Task ListenAsync_PartialSegmentParameter_FailsWithoutConnecting()
    {
        var result = await Listener.ListenAsync(Connection(ConnectionServiceType.Kafka, "127.0.0.1:1"), "orders.eu-{region}.created:send", TimeSpan.FromSeconds(1), "amq.topic", TestContext.Current.CancellationToken);

        Assert.False(result.Received);
        Assert.Contains("isn't a whole segment", result.Message);
    }

    private static Connection Connection(ConnectionServiceType serviceType, string value)
        => new() { Id = Guid.NewGuid(), Name = "Target", ServiceType = serviceType, Value = value };
}
