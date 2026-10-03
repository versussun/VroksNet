using VroksNet.Application.Abstractions;
using VroksNet.Domain.Connections;

namespace VroksNet.UnitTests.TestDoubles;

internal sealed class FakeMessageListener(MessageListenResult result) : IMessageListener
{
    /// <summary><c>Exchange</c> is the <c>exchange</c> broker option, if any — null means the adapter's default.</summary>
    public (Connection Connection, string OperationKey, TimeSpan Timeout, string? Exchange)? LastListen { get; private set; }

    public Task<MessageListenResult> ListenAsync(Connection connection, string operationKey, TimeSpan timeout, BrokerOptions? options, CancellationToken cancellationToken, Action? onListening = null)
    {
        LastListen = (connection, operationKey, timeout, options?["exchange"]);
        if (result.Received)
        {
            onListening?.Invoke();
        }

        return Task.FromResult(result);
    }
}
