using VroksNet.Application.Abstractions;
using VroksNet.Domain.Connections;

namespace VroksNet.UnitTests.TestDoubles;

internal sealed class FakeMessageSender(MessageSendResult result) : IMessageSender
{
    public (Connection Connection, string OperationKey, string? Payload, string? Exchange)? LastSend { get; private set; }

    public Task<MessageSendResult> SendAsync(Connection connection, string operationKey, string? payload, string? exchange, CancellationToken cancellationToken)
    {
        LastSend = (connection, operationKey, payload, exchange);
        return Task.FromResult(result);
    }
}
