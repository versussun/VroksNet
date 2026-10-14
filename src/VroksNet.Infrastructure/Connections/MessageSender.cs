using VroksNet.Application.Abstractions;
using VroksNet.Domain.Connections;
using VroksNet.Infrastructure.Brokers;

namespace VroksNet.Infrastructure.Connections;

/// <summary>
/// Actually sends a message to a <see cref="Connection"/>'s target, through the
/// <see cref="IBrokerAdapter"/> for its type (ADR 0003): an HTTP request for
/// <see cref="ConnectionServiceType.Http"/>, a gRPC call for <see cref="ConnectionServiceType.Grpc"/>,
/// a real broker publish for the others.
/// </summary>
public sealed class MessageSender(BrokerAdapterRegistry adapters) : IMessageSender
{
    public Task<MessageSendResult> SendAsync(Connection connection, string operationKey, string? payload, BrokerOptions? options, CancellationToken cancellationToken, RpcSendContext? rpc = null)
        => adapters.Find(connection.ServiceType) is { } adapter
            ? adapter.SendAsync(connection, operationKey, payload, options, cancellationToken, rpc)
            : Task.FromResult(new MessageSendResult(false, $"Unsupported service type '{connection.ServiceType}'."));
}
