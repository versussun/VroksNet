using VroksNet.Application.Abstractions;
using VroksNet.Domain.Connections;
using VroksNet.Infrastructure.Brokers;

namespace VroksNet.Infrastructure.Connections;

/// <summary>
/// Actually reaches a <see cref="Connection"/>'s target, through the
/// <see cref="IBrokerAdapter"/> for its type (ADR 0003). This never touches the Aspire-wired dev
/// "rabbitmq"/"nats"/"kafka" resources from AppHost.cs — the adapters connect to whatever
/// host/credentials the user typed into <see cref="Connection.Value"/>.
/// </summary>
public sealed class ConnectionTester(BrokerAdapterRegistry adapters) : IConnectionTester
{
    public Task<ConnectionTestResult> TestAsync(Connection connection, CancellationToken cancellationToken)
        => adapters.Find(connection.ServiceType) is { } adapter
            ? adapter.TestAsync(connection, cancellationToken)
            : Task.FromResult(new ConnectionTestResult(false, $"Unsupported service type '{connection.ServiceType}'."));
}
