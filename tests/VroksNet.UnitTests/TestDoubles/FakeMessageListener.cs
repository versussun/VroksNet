using VroksNet.Application.Abstractions;
using VroksNet.Domain.Connections;

namespace VroksNet.UnitTests.TestDoubles;

internal sealed class FakeMessageListener(MessageListenResult result) : IMessageListener
{
    public (Connection Connection, string OperationKey, TimeSpan Timeout, string Exchange)? LastListen { get; private set; }

    public Task<MessageListenResult> ListenAsync(Connection connection, string operationKey, TimeSpan timeout, string exchange, CancellationToken cancellationToken, Action? onListening = null)
    {
        LastListen = (connection, operationKey, timeout, exchange);
        if (result.Received)
        {
            onListening?.Invoke();
        }

        return Task.FromResult(result);
    }
}
