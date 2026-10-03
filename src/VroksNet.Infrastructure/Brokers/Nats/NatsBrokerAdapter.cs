using NATS.Client.Core;
using VroksNet.Application.Abstractions;
using VroksNet.Domain.Connections;
using VroksNet.Domain.TestScenarios;

namespace VroksNet.Infrastructure.Brokers.Nats;

/// <summary>
/// <see cref="ConnectionServiceType.Nats"/>. The connection value is a nats:// URL. Send publishes
/// to subject = the operation's channel address and pings afterwards, since a publish only
/// buffers; Listen is a plain core subscription to the channel address with each parameter as
/// "*". NATS wildcards stand for whole "."-separated tokens, so a channel with parameters between
/// "/"-separated segments can't be listened on here.
/// </summary>
public sealed class NatsBrokerAdapter : IListeningBrokerAdapter
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(10);

    public ConnectionServiceType Type => ConnectionServiceType.Nats;

    public IReadOnlyList<BrokerOptionDefinition> Options => [];

    public async Task<ConnectionTestResult> TestAsync(Connection connection, CancellationToken cancellationToken)
    {
        try
        {
            await using var natsConnection = new NatsConnection(new NatsOpts { Url = connection.Value });
            var roundTrip = await natsConnection.PingAsync(cancellationToken).AsTask().WaitAsync(TestTimeout, cancellationToken);
            return new ConnectionTestResult(true, $"Connected — round trip {roundTrip.TotalMilliseconds:0}ms.");
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

    public async Task<MessageSendResult> SendAsync(Connection connection, string operationKey, string? payload, BrokerOptions? options, CancellationToken cancellationToken)
    {
        var subject = OperationCompatibility.ChannelAddressOf(operationKey);
        if (subject is null)
        {
            return new MessageSendResult(false, "This operation isn't AsyncAPI-shaped (expected \"channel:action\") — it can't be published to a Nats connection.");
        }

        try
        {
            await using var natsConnection = new NatsConnection(new NatsOpts { Url = connection.Value });
            // PublishAsync only buffers the message; the PONG proves the server has it (it handles
            // commands in order) before the connection is disposed, so "published" is true.
            await PublishAndConfirmAsync(natsConnection, subject, payload ?? string.Empty, cancellationToken).WaitAsync(SendTimeout, cancellationToken);
            return new MessageSendResult(true, $"Published to subject \"{subject}\".");
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

    public async Task<MessageListenResult> ListenAsync(Connection connection, ChannelPattern channel, TimeSpan timeout, BrokerOptions? options, CancellationToken cancellationToken, Action? onListening = null)
    {
        if (SubjectOf(channel) is not { } subject)
        {
            return new MessageListenResult(false, CantListenMessage(channel));
        }

        var stage = ListenStage.Connecting;
        try
        {
            await using var natsConnection = new NatsConnection(new NatsOpts { Url = connection.Value, ConnectTimeout = BrokerListening.ConnectTimeout });
            await natsConnection.ConnectAsync();
            stage = ListenStage.SettingUp;

            using var setupCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            setupCts.CancelAfter(BrokerListening.ConnectTimeout);
            // The token SubscribeCoreAsync gets ends the subscription when it fires, so it's the
            // run's own token; the setup deadline only bounds the wait for the subscription.
            await using var subscription = await natsConnection.SubscribeCoreAsync<string>(subject, cancellationToken: cancellationToken).AsTask().WaitAsync(setupCts.Token);

            // SubscribeCoreAsync only queues SUB; the server handles commands in order, so the PONG
            // proves the subscription is registered before the listen window starts.
            await natsConnection.PingAsync(setupCts.Token);
            stage = ListenStage.Listening;
            onListening?.Invoke();

            // Cancelling the read itself (rather than abandoning it via WaitAsync) leaves no
            // pending read behind to fault unobserved when the subscription is disposed.
            using var listenCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            listenCts.CancelAfter(timeout);
            var message = await subscription.Msgs.ReadAsync(listenCts.Token);
            return new MessageListenResult(true, $"Received on subject \"{message.Subject}\".", message.Data);
        }
        catch (Exception ex) when (ex is TimeoutException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return new MessageListenResult(false, BrokerListening.TimeoutMessage(stage, $"subject \"{subject}\"", timeout));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new MessageListenResult(false, ex.Message);
        }
    }

    public string? WhyCantListen(ChannelPattern channel, BrokerOptions? options) => SubjectOf(channel) is null ? CantListenMessage(channel) : null;

    private static string CantListenMessage(ChannelPattern channel)
        => $"Channel \"{channel.Address}\" has parameters between \"/\"-separated segments, and NATS wildcards only stand for whole \".\"-separated tokens — it can't be listened on through a Nats connection.";

    /// <summary>The subject to subscribe to for <paramref name="channel"/> ("orders.{region}.created" → "orders.*.created"); null if it has parameters but isn't "."-separated.</summary>
    public static string? SubjectOf(ChannelPattern channel)
        => channel.HasParameters && channel.Separator != '.' ? null : channel.Render("*");

    private static async Task PublishAndConfirmAsync(NatsConnection connection, string subject, string payload, CancellationToken cancellationToken)
    {
        await connection.PublishAsync(subject, payload, cancellationToken: cancellationToken);
        await connection.PingAsync(cancellationToken);
    }
}
