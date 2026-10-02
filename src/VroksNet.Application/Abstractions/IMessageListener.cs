using VroksNet.Domain.Connections;

namespace VroksNet.Application.Abstractions;

/// <summary>
/// The receiving counterpart of <see cref="IMessageSender"/>: waits for the next message on an
/// AsyncAPI operation's channel ("channel/address:action") through a RabbitMq/Nats
/// <see cref="Connection"/>. Short-lived — subscribes for one run and cleans up after itself,
/// without taking messages away from the channel's real consumers.
/// </summary>
public interface IMessageListener
{
    /// <param name="exchange">RabbitMQ only: the exchange to bind a temporary queue to (binding key = channel address).</param>
    Task<MessageListenResult> ListenAsync(Connection connection, string operationKey, TimeSpan timeout, string exchange, CancellationToken cancellationToken);
}
