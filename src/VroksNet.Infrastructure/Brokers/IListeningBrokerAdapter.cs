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
    /// <summary>Why its broker can't subscribe to <paramref name="channel"/> with the scenario's <paramref name="options"/> (e.g. its wildcards can't match the channel's parameters); null if it can.</summary>
    string? WhyCantListen(ChannelPattern channel, BrokerOptions? options);

    /// <param name="channel">
    /// The channel to subscribe to. The adapter renders it in its broker's own wildcard syntax, and
    /// fails readably if its broker can't match it (e.g. a parameter between "/"-separated segments
    /// where wildcards only exist for "."-separated ones).
    /// </param>
    /// <param name="options">The scenario's broker options; the adapter applies its own defaults.</param>
    /// <param name="onListening">
    /// Called exactly when the subscription is in place — see <see cref="IMessageListener"/>.
    /// </param>
    Task<MessageListenResult> ListenAsync(Connection connection, ChannelPattern channel, TimeSpan timeout, BrokerOptions? options, CancellationToken cancellationToken, Action? onListening = null);
}
