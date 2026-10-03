using VroksNet.Domain.Connections;

namespace VroksNet.Application.Abstractions;

/// <summary>
/// The receiving counterpart of <see cref="IMessageSender"/>: waits for the next message on an
/// AsyncAPI operation's channel ("channel/address:action") through a RabbitMq/Nats/Kafka
/// <see cref="Connection"/>. Short-lived — subscribes for one run and cleans up after itself,
/// without taking messages away from the channel's real consumers.
/// </summary>
public interface IMessageListener
{
    /// <param name="options">The scenario's broker options (ADR 0003), e.g. RabbitMQ's <c>exchange</c> to bind a temporary queue to; the adapter applies its own defaults.</param>
    /// <param name="onListening">
    /// Called once the subscription is in place and the listen window has opened — a message sent
    /// after that is guaranteed to be seen. Not called if the listen fails before getting there.
    /// A test suite uses it to start its Sends only once every Listen is ready (ADR 0002).
    /// </param>
    Task<MessageListenResult> ListenAsync(Connection connection, string operationKey, TimeSpan timeout, BrokerOptions? options, CancellationToken cancellationToken, Action? onListening = null);
}
