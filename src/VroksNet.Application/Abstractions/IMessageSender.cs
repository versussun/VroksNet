using VroksNet.Domain.Connections;

namespace VroksNet.Application.Abstractions;

/// <summary>
/// Actually sends a message to a <see cref="Connection"/>'s target: an HTTP request built from an
/// operation key ("METHOD /path") for <see cref="ConnectionServiceType.Http"/>, a gRPC call
/// ("RPC /package.Service/Method") for <see cref="ConnectionServiceType.Grpc"/>, or a broker
/// publish built from one ("channel/address:action") for the brokers. <c>options</c> are the
/// scenario's or Publisher's broker options (ADR 0003), e.g. RabbitMQ's <c>exchange</c>; the
/// connection type's adapter reads the ones it declares and applies its own defaults.
/// <c>rpc</c> carries what a gRPC call needs to write its request message (ADR 0004); only the
/// gRPC adapter reads it.
/// </summary>
public interface IMessageSender
{
    Task<MessageSendResult> SendAsync(Connection connection, string operationKey, string? payload, BrokerOptions? options, CancellationToken cancellationToken, RpcSendContext? rpc = null);
}

/// <summary>
/// <see cref="Message"/> is safe to show verbatim in the UI. <see cref="ResponseBody"/> is only
/// ever populated for an HTTP send (the real response body) — a broker publish has no synchronous
/// response to show; likewise <see cref="StatusCode"/>, the HTTP response's status code. A gRPC
/// call sets <see cref="GrpcStatus"/> instead (any status the server answered with) and, on
/// <c>OK</c>, <see cref="ResponseMessage"/>: the response message's bytes, which the caller decodes
/// with the same descriptors. <see cref="Success"/> only means the send itself went through (any
/// HTTP response counts; for gRPC only <c>OK</c>) — whether the response matches the spec is
/// checked separately by the caller.
/// </summary>
public sealed record MessageSendResult(
    bool Success,
    string Message,
    string? ResponseBody = null,
    int? StatusCode = null,
    int? GrpcStatus = null,
    byte[]? ResponseMessage = null);
