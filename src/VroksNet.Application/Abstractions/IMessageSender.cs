using VroksNet.Domain.Connections;

namespace VroksNet.Application.Abstractions;

/// <summary>
/// Actually sends a message to a <see cref="Connection"/>'s target: an HTTP request built from an
/// operation key ("METHOD /path") for <see cref="ConnectionServiceType.Http"/>, or a broker
/// publish built from one ("channel/address:action") for the brokers. <c>options</c> are the
/// scenario's or Publisher's broker options (ADR 0003), e.g. RabbitMQ's <c>exchange</c>; the
/// connection type's adapter reads the ones it declares and applies its own defaults.
/// </summary>
public interface IMessageSender
{
    Task<MessageSendResult> SendAsync(Connection connection, string operationKey, string? payload, BrokerOptions? options, CancellationToken cancellationToken);
}

/// <summary>
/// <see cref="Message"/> is safe to show verbatim in the UI. <see cref="ResponseBody"/> is only
/// ever populated for an HTTP send (the real response body) — a broker publish has no synchronous
/// response to show; likewise <see cref="StatusCode"/>, the HTTP response's status code.
/// <see cref="Success"/> only means the send itself went through (any HTTP response counts) —
/// whether the response matches the spec is checked separately by the caller.
/// </summary>
public sealed record MessageSendResult(bool Success, string Message, string? ResponseBody = null, int? StatusCode = null);
