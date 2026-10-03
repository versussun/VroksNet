using VroksNet.Application.Abstractions;
using VroksNet.Domain.Connections;

namespace VroksNet.Infrastructure.Brokers;

/// <summary>
/// A <see cref="IBrokerAdapter"/> whose type can also be listened on, for
/// <see cref="Connections.MessageListener"/>. Listening must never take messages from the
/// channel's real consumers: a fan-out mechanism (a temporary queue or subscription) or nothing.
/// </summary>
public interface IListeningBrokerAdapter : IBrokerAdapter
{
    /// <param name="subscriptionPattern">
    /// The channel address with whole-segment parameters turned into "*"
    /// (<see cref="Domain.TestScenarios.TestScenarioListening.SubscriptionPatternOf"/>).
    /// </param>
    /// <param name="exchange">RabbitMQ only: the exchange to bind a temporary queue to.</param>
    /// <param name="onListening">
    /// Called exactly when the subscription is in place — see <see cref="IMessageListener"/>.
    /// </param>
    Task<MessageListenResult> ListenAsync(Connection connection, string subscriptionPattern, TimeSpan timeout, string exchange, CancellationToken cancellationToken, Action? onListening = null);
}
