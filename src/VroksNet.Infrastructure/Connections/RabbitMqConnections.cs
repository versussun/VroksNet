using RabbitMQ.Client;

namespace VroksNet.Infrastructure.Connections;

/// <summary>
/// Opens the short-lived RabbitMQ connections <see cref="ConnectionTester"/>,
/// <see cref="MessageSender"/> and <see cref="MessageListener"/> use. The client's own timeouts are
/// set to match and the attempt is *cancelled* on timeout (not merely abandoned via WaitAsync), so a
/// slow broker can't leave a connection opening in the background. Automatic recovery is off: a
/// connection that lives for one operation has nothing to recover, and recovery would keep an
/// orphaned one alive for the life of the process.
/// </summary>
internal static class RabbitMqConnections
{
    /// <exception cref="TimeoutException">The broker didn't accept the connection within <paramref name="timeout"/>.</exception>
    public static async Task<IConnection> OpenAsync(Uri uri, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory
        {
            Uri = uri,
            RequestedConnectionTimeout = timeout,
            HandshakeContinuationTimeout = timeout,
            AutomaticRecoveryEnabled = false,
            TopologyRecoveryEnabled = false
        };

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            return await factory.CreateConnectionAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Timed out connecting to the broker after {timeout.TotalSeconds:0}s.");
        }
    }
}
