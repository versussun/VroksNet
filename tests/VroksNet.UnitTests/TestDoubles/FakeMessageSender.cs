using VroksNet.Application.Abstractions;
using VroksNet.Domain.Connections;

namespace VroksNet.UnitTests.TestDoubles;

internal sealed class FakeMessageSender(MessageSendResult result) : IMessageSender
{
    /// <summary><c>Exchange</c> is the <c>exchange</c> broker option, if any.</summary>
    public (Connection Connection, string OperationKey, string? Payload, string? Exchange)? LastSend { get; private set; }

    /// <summary>The gRPC context the last send was given, if any.</summary>
    public RpcSendContext? LastRpc { get; private set; }

    public Task<MessageSendResult> SendAsync(Connection connection, string operationKey, string? payload, BrokerOptions? options, CancellationToken cancellationToken, RpcSendContext? rpc = null)
    {
        LastSend = (connection, operationKey, payload, options?["exchange"]);
        LastRpc = rpc;
        return Task.FromResult(result);
    }
}
