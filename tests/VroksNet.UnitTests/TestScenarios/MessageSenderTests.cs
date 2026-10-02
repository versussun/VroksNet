using Microsoft.Extensions.DependencyInjection;
using VroksNet.Domain.Connections;
using VroksNet.Infrastructure.Connections;

namespace VroksNet.UnitTests.TestScenarios;

/// <summary>
/// Exercises the real <see cref="MessageSender"/> (not a fake) — no Docker/AppHost needed, same
/// approach as <see cref="Connections.ConnectionTesterTests"/>: only ever connects to
/// definitely-closed local ports or a domain RFC 2606 reserves to never resolve, so it exercises
/// the failure path fast and deterministically, offline.
/// </summary>
public class MessageSenderTests
{
    private static readonly MessageSender Sender = new(
        new ServiceCollection().AddHttpClient().BuildServiceProvider().GetRequiredService<IHttpClientFactory>());

    [Fact]
    public async Task SendAsync_HttpOperationThroughRabbitMqConnection_FailsWithoutAttemptingAnyRequest()
    {
        var result = await Sender.SendAsync(Connection("RabbitMq", "amqp://guest:guest@127.0.0.1:1"), "GET /pets", null, null, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("AsyncAPI-shaped", result.Message);
    }

    [Fact]
    public async Task SendAsync_AsyncApiOperationThroughHttpConnection_FailsWithoutAttemptingAnyRequest()
    {
        var result = await Sender.SendAsync(Connection("Http", "https://this-host-does-not-exist.invalid"), "orders.created:send", null, null, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("HTTP-shaped", result.Message);
    }

    [Fact]
    public async Task SendAsync_Http_UnresolvableHost_Fails()
    {
        var result = await Sender.SendAsync(Connection("Http", "https://this-host-does-not-exist.invalid"), "GET /pets", "[]", null, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public async Task SendAsync_RabbitMq_ClosedPort_Fails()
    {
        var result = await Sender.SendAsync(Connection("RabbitMq", "amqp://guest:guest@127.0.0.1:1"), "orders.created:send", "{}", null, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public async Task SendAsync_HttpOperationThroughKafkaConnection_FailsWithoutAttemptingAnyRequest()
    {
        var result = await Sender.SendAsync(Connection("Kafka", "127.0.0.1:1"), "GET /pets", null, null, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("AsyncAPI-shaped", result.Message);
    }

    [Fact]
    public async Task SendAsync_Kafka_ClosedPort_FailsWithinTheTimeout()
    {
        var result = await Sender.SendAsync(Connection("Kafka", "127.0.0.1:1"), "orders.created:send", "{}", null, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public async Task SendAsync_Nats_ClosedPort_Fails()
    {
        var result = await Sender.SendAsync(Connection("Nats", "nats://127.0.0.1:1"), "orders.created:send", "{}", null, TestContext.Current.CancellationToken);

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
