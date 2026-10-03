using VroksNet.Application.Abstractions;
using VroksNet.Domain.Connections;
using VroksNet.Domain.TestScenarios;
using VroksNet.Infrastructure.Brokers;

namespace VroksNet.Infrastructure.Connections;

/// <summary>
/// Waits for the next message on an AsyncAPI operation's channel, through the
/// <see cref="IListeningBrokerAdapter"/> for the connection's type (ADR 0003). The adapter gets the
/// channel as a <see cref="ChannelPattern"/>, renders it in its broker's own wildcards, and never
/// takes messages from the channel's real consumers.
/// </summary>
public sealed class MessageListener(BrokerAdapterRegistry adapters) : IMessageListener
{
    public Task<MessageListenResult> ListenAsync(Connection connection, string operationKey, TimeSpan timeout, BrokerOptions? options, CancellationToken cancellationToken, Action? onListening = null)
    {
        var channelAddress = OperationCompatibility.ChannelAddressOf(operationKey);
        if (channelAddress is null)
        {
            return Task.FromResult(new MessageListenResult(false, "This operation isn't AsyncAPI-shaped (expected \"channel:action\") — there's no channel to listen to."));
        }

        var pattern = ChannelPattern.Parse(channelAddress);
        if (pattern is null)
        {
            return Task.FromResult(new MessageListenResult(false, $"Channel \"{channelAddress}\" has a parameter that isn't a whole segment, so it can't be subscribed to."));
        }

        return adapters.Find(connection.ServiceType) is IListeningBrokerAdapter adapter
            ? adapter.ListenAsync(connection, pattern, timeout, options, cancellationToken, onListening)
            : Task.FromResult(new MessageListenResult(false, $"Can't listen through a {connection.ServiceType} connection. {ServiceTypeTraits.Find(connection.ServiceType)?.ListenNote}".TrimEnd()));
    }
}
