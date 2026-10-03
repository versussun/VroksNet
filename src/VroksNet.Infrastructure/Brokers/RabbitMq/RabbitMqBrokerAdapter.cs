using System.Text;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using VroksNet.Application.Abstractions;
using VroksNet.Domain.Connections;
using VroksNet.Domain.TestScenarios;

namespace VroksNet.Infrastructure.Brokers.RabbitMq;

/// <summary>
/// <see cref="ConnectionServiceType.RabbitMq"/>. The connection value is an amqp(s):// URI; every
/// connection is opened through <see cref="RabbitMqConnections.OpenAsync"/>.
/// <list type="bullet">
/// <item>Send publishes with routing key = the operation's channel address, to the given exchange
/// or the default one (straight into the queue named after the channel). A non-default exchange
/// is checked first: publishing to a missing one isn't an error the publish reports.</item>
/// <item>Listen binds a server-named exclusive, auto-delete queue to the exchange, so the broker
/// hands this run its own copy and the queue goes away with the connection. On a topic exchange
/// the binding key is a pattern; a fanout/headers exchange ignores it, so any message on the
/// exchange counts.</item>
/// </list>
/// </summary>
public sealed class RabbitMqBrokerAdapter : IListeningBrokerAdapter
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(10);

    public ConnectionServiceType Type => ConnectionServiceType.RabbitMq;

    public async Task<ConnectionTestResult> TestAsync(Connection connection, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(connection.Value, UriKind.Absolute, out var uri))
        {
            return new ConnectionTestResult(false, "Not a valid amqp(s):// connection string.");
        }

        try
        {
            await using var brokerConnection = await RabbitMqConnections.OpenAsync(uri, TestTimeout, cancellationToken);
            return new ConnectionTestResult(true, $"Connected to {brokerConnection.Endpoint}.");
        }
        catch (TimeoutException)
        {
            return new ConnectionTestResult(false, $"Timed out after {TestTimeout.TotalSeconds:0}s.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ConnectionTestResult(false, ex.Message);
        }
    }

    public async Task<MessageSendResult> SendAsync(Connection connection, string operationKey, string? payload, string? exchange, CancellationToken cancellationToken)
    {
        exchange ??= string.Empty;
        var channelAddress = OperationCompatibility.ChannelAddressOf(operationKey);
        if (channelAddress is null)
        {
            return new MessageSendResult(false, "This operation isn't AsyncAPI-shaped (expected \"channel:action\") — it can't be published to a RabbitMq connection.");
        }

        if (!Uri.TryCreate(connection.Value, UriKind.Absolute, out var uri))
        {
            return new MessageSendResult(false, "Not a valid amqp(s):// connection string.");
        }

        try
        {
            await using var brokerConnection = await RabbitMqConnections.OpenAsync(uri, SendTimeout, cancellationToken);
            await using var channel = await brokerConnection.CreateChannelAsync(cancellationToken: cancellationToken);

            // Publishing to a missing exchange isn't an error the publish reports — the broker closes
            // the channel afterwards — so check it exists first. The default exchange ("") always
            // does; it routes by routing key = queue name.
            if (exchange.Length > 0)
            {
                await channel.ExchangeDeclarePassiveAsync(exchange, cancellationToken);
            }

            await channel.BasicPublishAsync(
                exchange: exchange,
                routingKey: channelAddress,
                body: Encoding.UTF8.GetBytes(payload ?? string.Empty),
                cancellationToken: cancellationToken);

            return new MessageSendResult(true, exchange.Length > 0
                ? $"Published to exchange \"{exchange}\", routing key \"{channelAddress}\"."
                : $"Published to routing key \"{channelAddress}\".");
        }
        catch (OperationInterruptedException ex) when (ex.ShutdownReason?.ReplyCode == 404)
        {
            return new MessageSendResult(false, $"Exchange \"{exchange}\" doesn't exist on this broker.");
        }
        catch (TimeoutException)
        {
            return new MessageSendResult(false, $"Timed out after {SendTimeout.TotalSeconds:0}s.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new MessageSendResult(false, ex.Message);
        }
    }

    public async Task<MessageListenResult> ListenAsync(Connection connection, string subscriptionPattern, TimeSpan timeout, string exchange, CancellationToken cancellationToken, Action? onListening = null)
    {
        if (!Uri.TryCreate(connection.Value, UriKind.Absolute, out var uri))
        {
            return new MessageListenResult(false, "Not a valid amqp(s):// connection string.");
        }

        var bindingKey = subscriptionPattern;
        var stage = ListenStage.Connecting;
        try
        {
            await using var brokerConnection = await RabbitMqConnections.OpenAsync(uri, BrokerListening.ConnectTimeout, cancellationToken);
            stage = ListenStage.SettingUp;

            // Setup gets its own deadline, so a broker that accepts the connection but stalls on
            // declare/bind/consume can't hold the run beyond the connect budget.
            using var setupCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            setupCts.CancelAfter(BrokerListening.ConnectTimeout);

            await using var channel = await brokerConnection.CreateChannelAsync(cancellationToken: setupCts.Token);
            var queue = await channel.QueueDeclareAsync(queue: string.Empty, durable: false, exclusive: true, autoDelete: true, cancellationToken: setupCts.Token);
            await channel.QueueBindAsync(queue.QueueName, exchange, bindingKey, cancellationToken: setupCts.Token);

            var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += (_, delivery) =>
            {
                // The body buffer is only valid during this callback — decode it now.
                received.TrySetResult(Encoding.UTF8.GetString(delivery.Body.Span));
                return Task.CompletedTask;
            };
            await channel.BasicConsumeAsync(queue.QueueName, autoAck: true, consumer, setupCts.Token);
            stage = ListenStage.Listening;
            onListening?.Invoke();

            var payload = await received.Task.WaitAsync(timeout, cancellationToken);
            return new MessageListenResult(true, $"Received on exchange \"{exchange}\", routing key matching \"{bindingKey}\".", payload);
        }
        catch (OperationInterruptedException ex) when (ex.ShutdownReason?.ReplyCode == 404)
        {
            return new MessageListenResult(false, $"Exchange \"{exchange}\" doesn't exist on this broker.");
        }
        catch (Exception ex) when (ex is TimeoutException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return new MessageListenResult(false, BrokerListening.TimeoutMessage(stage, $"exchange \"{exchange}\", routing key \"{bindingKey}\"", timeout));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new MessageListenResult(false, ex.Message);
        }
    }
}
