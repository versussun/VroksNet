using VroksNet.Application.Abstractions;
using VroksNet.Domain.Connections;

namespace VroksNet.UnitTests.TestDoubles;

internal sealed class FakeMessageSender(MessageSendResult result) : IMessageSender
{
    public (Connection Connection, string OperationKey, string? Payload)? LastSend { get; private set; }

    public Task<MessageSendResult> SendAsync(Connection connection, string operationKey, string? payload, CancellationToken cancellationToken)
    {
        LastSend = (connection, operationKey, payload);
        return Task.FromResult(result);
    }
}
