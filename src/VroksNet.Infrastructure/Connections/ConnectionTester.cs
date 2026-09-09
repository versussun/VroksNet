using NATS.Client.Core;
using RabbitMQ.Client;
using VroksNet.Application.Abstractions;
using VroksNet.Domain.Connections;

namespace VroksNet.Infrastructure.Connections;

/// <summary>
/// Actually reaches a <see cref="Connection"/>'s target: a short-timeout GET for
/// <see cref="ConnectionServiceType.Http"/> (any HTTP response counts as reachable — even a
/// 404/401 proves DNS+TCP+TLS all worked, which is the point; only a thrown exception counts as
/// failure), or opening a real client connection and pinging it for RabbitMq/Nats. This never
/// touches the Aspire-wired dev "rabbitmq"/"nats" resources from AppHost.cs — it connects to
/// whatever host/credentials the user typed into <see cref="Connection.Value"/>.
/// </summary>
public sealed class ConnectionTester(IHttpClientFactory httpClientFactory) : IConnectionTester
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public Task<ConnectionTestResult> TestAsync(Connection connection, CancellationToken cancellationToken) => connection.ServiceType switch
    {
        ConnectionServiceType.Http => TestHttpAsync(connection.Value, cancellationToken),
        ConnectionServiceType.RabbitMq => TestRabbitMqAsync(connection.Value, cancellationToken),
        ConnectionServiceType.Nats => TestNatsAsync(connection.Value, cancellationToken),
        _ => Task.FromResult(new ConnectionTestResult(false, $"Unsupported service type '{connection.ServiceType}'."))
    };

    private async Task<ConnectionTestResult> TestHttpAsync(string url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return new ConnectionTestResult(false, "Not a valid absolute URL.");
        }

        try
        {
            var client = httpClientFactory.CreateClient(nameof(ConnectionTester));
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .WaitAsync(Timeout, cancellationToken);
            return new ConnectionTestResult(true, $"Reached {uri.Host} — responded {(int)response.StatusCode} {response.StatusCode}.");
        }
        catch (TimeoutException)
        {
            return new ConnectionTestResult(false, $"Timed out after {Timeout.TotalSeconds:0}s.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ConnectionTestResult(false, ex.Message);
        }
    }

    private static async Task<ConnectionTestResult> TestRabbitMqAsync(string connectionString, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(connectionString, UriKind.Absolute, out var uri))
        {
            return new ConnectionTestResult(false, "Not a valid amqp(s):// connection string.");
        }

        try
        {
            var factory = new ConnectionFactory { Uri = uri };
            await using var connection = await factory.CreateConnectionAsync(cancellationToken).WaitAsync(Timeout, cancellationToken);
            return new ConnectionTestResult(true, $"Connected to {connection.Endpoint}.");
        }
        catch (TimeoutException)
        {
            return new ConnectionTestResult(false, $"Timed out after {Timeout.TotalSeconds:0}s.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ConnectionTestResult(false, ex.Message);
        }
    }

    private static async Task<ConnectionTestResult> TestNatsAsync(string connectionString, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new NatsConnection(new NatsOpts { Url = connectionString });
            var roundTrip = await connection.PingAsync(cancellationToken).AsTask().WaitAsync(Timeout, cancellationToken);
            return new ConnectionTestResult(true, $"Connected — round trip {roundTrip.TotalMilliseconds:0}ms.");
        }
        catch (TimeoutException)
        {
            return new ConnectionTestResult(false, $"Timed out after {Timeout.TotalSeconds:0}s.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ConnectionTestResult(false, ex.Message);
        }
    }
}
