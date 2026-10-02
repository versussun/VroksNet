using VroksNet.Application.Abstractions;
using VroksNet.Domain.Connections;

namespace VroksNet.UnitTests.TestDoubles;

internal sealed class FakeMessageListener(MessageListenResult result) : IMessageListener
{
    public (Connection Connection, string OperationKey, TimeSpan Timeout, string Exchange)? LastListen { get; private set; }

    public Task<MessageListenResult> ListenAsync(Connection connection, string operationKey, TimeSpan timeout, string exchange, CancellationToken cancellationToken)
    {
        LastListen = (connection, operationKey, timeout, exchange);
        return Task.FromResult(result);
    }
}
