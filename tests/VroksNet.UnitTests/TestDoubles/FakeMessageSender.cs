using VroksNet.Application.Abstractions;
using VroksNet.Domain.Connections;

namespace VroksNet.UnitTests.TestDoubles;

internal sealed class FakeMessageSender(MessageSendResult result) : IMessageSender
{
    /// <summary><c>Exchange</c> is the <c>exchange</c> broker option, if any.</summary>
    public (Connection Connection, string OperationKey, string? Payload, string? Exchange)? LastSend { get; private set; }

    public Task<MessageSendResult> SendAsync(Connection connection, string operationKey, string? payload, BrokerOptions? options, CancellationToken cancellationToken)
    {
        LastSend = (connection, operationKey, payload, options?["exchange"]);
        return Task.FromResult(result);
    }
}
