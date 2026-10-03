using VroksNet.Application.Abstractions;
using VroksNet.Domain.Connections;
using VroksNet.Domain.TestScenarios;
using VroksNet.Infrastructure.Brokers;

namespace VroksNet.Infrastructure.Connections;

/// <summary>
/// Waits for the next message on an AsyncAPI operation's channel, through the
/// <see cref="IListeningBrokerAdapter"/> for the connection's type (ADR 0003). Every adapter
/// subscribes with <see cref="TestScenarioListening.SubscriptionPatternOf"/> — the channel address
/// with whole-segment parameters turned into "*" — and never takes messages from the channel's
/// real consumers.
/// </summary>
public sealed class MessageListener(BrokerAdapterRegistry adapters) : IMessageListener
{
    public Task<MessageListenResult> ListenAsync(Connection connection, string operationKey, TimeSpan timeout, string exchange, CancellationToken cancellationToken, Action? onListening = null)
    {
        var channelAddress = OperationCompatibility.ChannelAddressOf(operationKey);
        if (channelAddress is null)
        {
            return Task.FromResult(new MessageListenResult(false, "This operation isn't AsyncAPI-shaped (expected \"channel:action\") — there's no channel to listen to."));
        }

        var pattern = TestScenarioListening.SubscriptionPatternOf(channelAddress);
        if (pattern is null)
        {
            return Task.FromResult(new MessageListenResult(false, $"Channel \"{channelAddress}\" has a parameter that isn't a whole \".\"-separated segment, so it can't be subscribed to."));
        }

        return adapters.Find(connection.ServiceType) is IListeningBrokerAdapter adapter
            ? adapter.ListenAsync(connection, pattern, timeout, exchange, cancellationToken, onListening)
            : Task.FromResult(new MessageListenResult(false, $"Can't listen through a {connection.ServiceType} connection — only RabbitMq/Nats/Kafka."));
    }
}
