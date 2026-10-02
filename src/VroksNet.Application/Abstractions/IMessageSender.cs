using VroksNet.Domain.Connections;

namespace VroksNet.Application.Abstractions;

/// <summary>
/// Actually sends a message to a <see cref="Connection"/>'s target: an HTTP request built from an
/// operation key ("METHOD /path") for <see cref="ConnectionServiceType.Http"/>, or a broker
/// publish built from one ("channel/address:action") for RabbitMq/Nats. <c>exchange</c> is the
/// RabbitMQ exchange to publish to (null or "" — the default exchange, i.e. straight into the
/// queue named after the channel); ignored for the other service types.
/// </summary>
public interface IMessageSender
{
    Task<MessageSendResult> SendAsync(Connection connection, string operationKey, string? payload, string? exchange, CancellationToken cancellationToken);
}

/// <summary>
/// <see cref="Message"/> is safe to show verbatim in the UI. <see cref="ResponseBody"/> is only
/// ever populated for an HTTP send (the real response body) — a broker publish has no synchronous
/// response to show; likewise <see cref="StatusCode"/>, the HTTP response's status code.
/// <see cref="Success"/> only means the send itself went through (any HTTP response counts) —
/// whether the response matches the spec is checked separately by the caller.
/// </summary>
public sealed record MessageSendResult(bool Success, string Message, string? ResponseBody = null, int? StatusCode = null);
