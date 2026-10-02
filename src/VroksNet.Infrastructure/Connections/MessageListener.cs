using System.Text;
using NATS.Client.Core;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using VroksNet.Application.Abstractions;
using VroksNet.Domain.Connections;
using VroksNet.Domain.TestScenarios;

namespace VroksNet.Infrastructure.Connections;

/// <summary>
/// Waits for the next message on an AsyncAPI operation's channel — the receiving sibling of
/// <see cref="MessageSender"/>, using the same direct clients (not the Aspire-wired dev brokers).
/// Never takes messages from the channel's real consumers:
/// <list type="bullet">
/// <item>RabbitMQ: a server-named exclusive, auto-delete queue bound to the given exchange, so the
/// broker hands this run its own copy. The queue goes away with the connection. On a topic
/// exchange the binding key is a pattern; a fanout/headers exchange ignores it, so any message on
/// the exchange counts.</item>
/// <item>NATS: a plain core subscription.</item>
/// </list>
/// Both subscribe with <see cref="TestScenarioListening.SubscriptionPatternOf"/> — the channel
/// address with whole-segment parameters turned into "*".
/// </summary>
public sealed class MessageListener : IMessageListener
{
    /// <summary>For reaching the broker and setting up the subscription — separate from (and not counted against) the listen timeout.</summary>
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

    public Task<MessageListenResult> ListenAsync(Connection connection, string operationKey, TimeSpan timeout, string exchange, CancellationToken cancellationToken)
    {
        var channelAddress = OperationCompatibility.ChannelAddressOf(operationKey);
        if (channelAddress is null)
        {
            return Task.FromResult(new MessageListenResult(false, "This operation isn't AsyncAPI-shaped (expected \"channel:action\") — there's no channel to listen to."));
        }

        var pattern = TestScenarioListening.SubscriptionPatternOf(channelAddress);
        if (pattern is null)
        {
            return Task.FromResult(new MessageListenResult(false, $"Channel \"{channelAddress}\" has a parameter that isn't a whole \".\"-separated segment, so it can't be subscribed to."));
        }

        return connection.ServiceType switch
        {
            ConnectionServiceType.RabbitMq => ListenRabbitMqAsync(connection.Value, pattern, exchange, timeout, cancellationToken),
            ConnectionServiceType.Nats => ListenNatsAsync(connection.Value, pattern, timeout, cancellationToken),
            _ => Task.FromResult(new MessageListenResult(false, $"Can't listen through a {connection.ServiceType} connection — only RabbitMq/Nats."))
        };
    }

    private static async Task<MessageListenResult> ListenRabbitMqAsync(string connectionString, string bindingKey, string exchange, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(connectionString, UriKind.Absolute, out var uri))
        {
            return new MessageListenResult(false, "Not a valid amqp(s):// connection string.");
        }

        var stage = ListenStage.Connecting;
        try
        {
            await using var connection = await RabbitMqConnections.OpenAsync(uri, ConnectTimeout, cancellationToken);
            stage = ListenStage.SettingUp;

            // Setup gets its own deadline, so a broker that accepts the connection but stalls on
            // declare/bind/consume can't hold the run beyond the connect budget.
            using var setupCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            setupCts.CancelAfter(ConnectTimeout);

            await using var channel = await connection.CreateChannelAsync(cancellationToken: setupCts.Token);
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

            var payload = await received.Task.WaitAsync(timeout, cancellationToken);
            return new MessageListenResult(true, $"Received on exchange \"{exchange}\", routing key matching \"{bindingKey}\".", payload);
        }
        catch (OperationInterruptedException ex) when (ex.ShutdownReason?.ReplyCode == 404)
        {
            return new MessageListenResult(false, $"Exchange \"{exchange}\" doesn't exist on this broker.");
        }
        catch (Exception ex) when (ex is TimeoutException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return new MessageListenResult(false, TimeoutMessage(stage, $"exchange \"{exchange}\", routing key \"{bindingKey}\"", timeout));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new MessageListenResult(false, ex.Message);
        }
    }

    private static async Task<MessageListenResult> ListenNatsAsync(string connectionString, string subject, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var stage = ListenStage.Connecting;
        try
        {
            await using var connection = new NatsConnection(new NatsOpts { Url = connectionString, ConnectTimeout = ConnectTimeout });
            await connection.ConnectAsync();
            stage = ListenStage.SettingUp;

            using var setupCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            setupCts.CancelAfter(ConnectTimeout);
            await using var subscription = await connection.SubscribeCoreAsync<string>(subject, cancellationToken: setupCts.Token);

            // SubscribeCoreAsync only queues SUB; the server handles commands in order, so the PONG
            // proves the subscription is registered before the listen window starts.
            await connection.PingAsync(setupCts.Token);
            stage = ListenStage.Listening;

            // Cancelling the read itself (rather than abandoning it via WaitAsync) leaves no
            // pending read behind to fault unobserved when the subscription is disposed.
            using var listenCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            listenCts.CancelAfter(timeout);
            var message = await subscription.Msgs.ReadAsync(listenCts.Token);
            return new MessageListenResult(true, $"Received on subject \"{message.Subject}\".", message.Data);
        }
        catch (Exception ex) when (ex is TimeoutException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return new MessageListenResult(false, TimeoutMessage(stage, $"subject \"{subject}\"", timeout));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new MessageListenResult(false, ex.Message);
        }
    }

    private static string TimeoutMessage(ListenStage stage, string target, TimeSpan timeout) => stage switch
    {
        ListenStage.Connecting => $"Timed out connecting to the broker after {ConnectTimeout.TotalSeconds:0}s.",
        ListenStage.SettingUp => $"Connected, but setting up the subscription on {target} timed out after {ConnectTimeout.TotalSeconds:0}s.",
        _ => $"No message on {target} within {timeout.TotalSeconds:0}s."
    };

    private enum ListenStage
    {
        Connecting,
        SettingUp,
        Listening
    }
}
