using VroksNet.Domain.Connections;

namespace VroksNet.Application.Abstractions;

/// <summary>
/// Actually attempts to reach a <see cref="Connection"/>'s target — a lightweight HTTP request
/// for <see cref="ConnectionServiceType.Http"/>, or opening (then closing) a real client
/// connection for <see cref="ConnectionServiceType.RabbitMq"/>/<see cref="ConnectionServiceType.Nats"/>/<see cref="ConnectionServiceType.Kafka"/>.
/// </summary>
public interface IConnectionTester
{
    Task<ConnectionTestResult> TestAsync(Connection connection, CancellationToken cancellationToken);
}

/// <summary><see cref="Message"/> is safe to show verbatim in the UI — never the raw connection string or a full exception stack.</summary>
public sealed record ConnectionTestResult(bool Success, string Message);
