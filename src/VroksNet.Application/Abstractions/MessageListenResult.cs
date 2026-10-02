namespace VroksNet.Application.Abstractions;

/// <summary>
/// <see cref="Received"/> is false on a timeout or a broker error, which <see cref="Message"/>
/// describes (UI-safe, like <see cref="MessageSendResult.Message"/>). <see cref="Payload"/> is the
/// received message body, decoded as UTF-8.
/// </summary>
public sealed record MessageListenResult(bool Received, string Message, string? Payload = null);
