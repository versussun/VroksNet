using VroksNet.Application.Abstractions;
using VroksNet.Domain.Connections;
using VroksNet.Domain.TestScenarios;

namespace VroksNet.Infrastructure.Brokers;

/// <summary>
/// A <see cref="IBrokerAdapter"/> whose type can also be listened on, for
/// <see cref="Connections.MessageListener"/>. Listening must never take messages from the
/// channel's real consumers: a fan-out mechanism (a temporary queue or subscription) or nothing.
/// </summary>
public interface IListeningBrokerAdapter : IBrokerAdapter
{
    /// <param name="channel">
    /// The channel to subscribe to. The adapter renders it in its broker's own wildcard syntax, and
    /// fails readably if its broker can't match it (e.g. a parameter between "/"-separated segments
    /// where wildcards only exist for "."-separated ones).
    /// </param>
    /// <param name="exchange">RabbitMQ only: the exchange to bind a temporary queue to.</param>
    /// <param name="onListening">
    /// Called exactly when the subscription is in place — see <see cref="IMessageListener"/>.
    /// </param>
    Task<MessageListenResult> ListenAsync(Connection connection, ChannelPattern channel, TimeSpan timeout, string exchange, CancellationToken cancellationToken, Action? onListening = null);
}
