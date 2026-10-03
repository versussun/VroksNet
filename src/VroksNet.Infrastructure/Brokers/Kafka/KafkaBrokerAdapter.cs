using System.Diagnostics;
using System.Text.RegularExpressions;
using Confluent.Kafka;
using VroksNet.Application.Abstractions;
using VroksNet.Domain.Connections;
using VroksNet.Domain.TestScenarios;

namespace VroksNet.Infrastructure.Brokers.Kafka;

/// <summary>
/// <see cref="ConnectionServiceType.Kafka"/>. Every client is built through
/// <see cref="KafkaClients"/>, which also parses the connection value. The check is a cluster
/// metadata request; Send produces to topic = the operation's channel address and waits for the
/// broker's acknowledgement. Listen uses no consumer group at all: the partitions of every
/// matching topic are assigned by hand, starting at their current end, and nothing is committed.
/// The topic's real consumer groups don't notice, and a message written after the listen window
/// opens can't be missed waiting for a group rebalance. The matching topics are found with a regex,
/// so a channel of either separator can be listened on.
/// </summary>
public sealed class KafkaBrokerAdapter : IListeningBrokerAdapter
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(10);

    public ConnectionServiceType Type => ConnectionServiceType.Kafka;

    public IReadOnlyList<BrokerOptionDefinition> Options => [];

    public async Task<ConnectionTestResult> TestAsync(Connection connection, CancellationToken cancellationToken)
    {
        var config = KafkaClients.ConfigFrom(connection.Value);
        if (config is null)
        {
            return new ConnectionTestResult(false, KafkaClients.InvalidConnectionStringMessage);
        }

        try
        {
            using var admin = KafkaClients.CreateAdminClient(config, TestTimeout);
            // GetMetadata blocks the calling thread for up to TestTimeout, so keep it off the request's.
            var metadata = await Task.Run(() => admin.GetMetadata(TestTimeout), cancellationToken);
            return new ConnectionTestResult(true, $"Connected — {metadata.Brokers.Count} broker(s) in the cluster.");
        }
        catch (KafkaException ex) when (ex.Error.Code is ErrorCode.Local_TimedOut or ErrorCode.Local_Transport or ErrorCode.Local_AllBrokersDown)
        {
            return new ConnectionTestResult(false, $"Couldn't reach the cluster within {TestTimeout.TotalSeconds:0}s ({ex.Error.Reason}).");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ConnectionTestResult(false, ex is KafkaException kafka ? kafka.Error.Reason : ex.Message);
        }
    }

    public async Task<MessageSendResult> SendAsync(Connection connection, string operationKey, string? payload, BrokerOptions? options, CancellationToken cancellationToken)
    {
        var topic = OperationCompatibility.ChannelAddressOf(operationKey);
        if (topic is null)
        {
            return new MessageSendResult(false, "This operation isn't AsyncAPI-shaped (expected \"channel:action\") — it can't be published to a Kafka connection.");
        }

        var config = KafkaClients.ConfigFrom(connection.Value);
        if (config is null)
        {
            return new MessageSendResult(false, KafkaClients.InvalidConnectionStringMessage);
        }

        try
        {
            using var producer = KafkaClients.CreateProducer(config, SendTimeout);
            // Completes once the broker acknowledges the write (or message.timeout.ms runs out),
            // so success here means the message really is on the topic.
            var delivery = await producer.ProduceAsync(topic, new Message<Null, string> { Value = payload ?? string.Empty }, cancellationToken);
            return new MessageSendResult(true, $"Published to topic \"{topic}\" (partition {delivery.Partition.Value}, offset {delivery.Offset.Value}).");
        }
        catch (ProduceException<Null, string> ex) when (ex.Error.Code == ErrorCode.Local_MsgTimedOut)
        {
            return new MessageSendResult(false, $"Timed out after {SendTimeout.TotalSeconds:0}s — the broker didn't acknowledge the message.");
        }
        catch (ProduceException<Null, string> ex) when (ex.Error.Code is ErrorCode.UnknownTopicOrPart or ErrorCode.Local_UnknownTopic)
        {
            return new MessageSendResult(false, $"Topic \"{topic}\" doesn't exist on this broker (and the broker doesn't auto-create topics).");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new MessageSendResult(false, ex is KafkaException kafka ? kafka.Error.Reason : ex.Message);
        }
    }

    public async Task<MessageListenResult> ListenAsync(Connection connection, ChannelPattern channel, TimeSpan timeout, BrokerOptions? options, CancellationToken cancellationToken, Action? onListening = null)
    {
        // What the messages show: the address with each parameter as "*".
        var pattern = channel.Render("*");
        var config = KafkaClients.ConfigFrom(connection.Value);
        if (config is null)
        {
            return new MessageListenResult(false, KafkaClients.InvalidConnectionStringMessage);
        }

        var topicRegex = TopicRegexOf(channel);
        var stage = ListenStage.Connecting;
        try
        {
            // The Kafka client's calls block the calling thread, so each runs via Task.Run.
            using var admin = KafkaClients.CreateAdminClient(config, BrokerListening.ConnectTimeout);
            var metadata = await Task.Run(() => admin.GetMetadata(BrokerListening.ConnectTimeout), cancellationToken);
            stage = ListenStage.SettingUp;

            var partitions = metadata.Topics
                .Where(topic => topic.Error.Code == ErrorCode.NoError && topicRegex.IsMatch(topic.Topic))
                .SelectMany(topic => topic.Partitions.Select(partition => new TopicPartition(topic.Topic, partition.PartitionId)))
                .ToList();
            if (partitions.Count == 0)
            {
                return new MessageListenResult(false, $"No topic matching \"{pattern}\" exists on this broker.");
            }

            using var consumer = KafkaClients.CreateConsumer(config, BrokerListening.ConnectTimeout);

            // Start each partition at its concrete high watermark rather than Offset.End: "end" is
            // only resolved once fetching starts, so a message written in between would be skipped.
            // The queries block one after another, so they share one setup deadline (each gets
            // what's left of it) instead of ConnectTimeout apiece.
            var start = await Task.Run(() => HighWatermarksOf(consumer, partitions, cancellationToken), cancellationToken);
            consumer.Assign(start);
            stage = ListenStage.Listening;
            onListening?.Invoke();

            // Consume(token) gives up by throwing OperationCanceledException — the read is
            // cancelled, not abandoned, so nothing is left polling once the consumer is disposed.
            using var listenCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            listenCts.CancelAfter(timeout);
            var result = await Task.Run(() => consumer.Consume(listenCts.Token), CancellationToken.None);
            return new MessageListenResult(true, $"Received on topic \"{result.Topic}\" (partition {result.Partition.Value}, offset {result.Offset.Value}).", result.Message.Value);
        }
        catch (KafkaException ex) when (ex.Error.Code is ErrorCode.Local_TimedOut or ErrorCode.Local_Transport or ErrorCode.Local_AllBrokersDown)
        {
            return new MessageListenResult(false, BrokerListening.TimeoutMessage(stage, $"topics matching \"{pattern}\"", timeout));
        }
        catch (Exception ex) when (ex is TimeoutException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return new MessageListenResult(false, BrokerListening.TimeoutMessage(stage, $"topics matching \"{pattern}\"", timeout));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new MessageListenResult(false, ex is KafkaException kafka ? kafka.Error.Reason : ex.Message);
        }
    }

    /// <summary>A topic regex can match a parameter between either separator, so any channel works.</summary>
    public string? WhyCantListen(ChannelPattern channel, BrokerOptions? options) => null;

    /// <summary>
    /// The regex for the topics <paramref name="channel"/> matches: each parameter matches one
    /// segment between the channel's separators, everything else literally ("orders.{region}.created"
    /// → ^orders\.[^.]+\.created$, "user/{id}/signedup" → ^user/[^/]+/signedup$).
    /// </summary>
    public static Regex TopicRegexOf(ChannelPattern channel)
    {
        var separator = Regex.Escape(channel.Separator.ToString());
        return new Regex("^" + string.Join(separator, channel.Segments.Select(segment => segment.IsParameter ? $"[^{separator}]+" : Regex.Escape(segment.Text))) + "$");
    }

    /// <exception cref="TimeoutException">The partitions' offsets weren't all known within <see cref="BrokerListening.ConnectTimeout"/>.</exception>
    private static List<TopicPartitionOffset> HighWatermarksOf(IConsumer<Ignore, string> consumer, List<TopicPartition> partitions, CancellationToken cancellationToken)
    {
        var setupClock = Stopwatch.StartNew();
        var offsets = new List<TopicPartitionOffset>(partitions.Count);
        foreach (var partition in partitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = BrokerListening.ConnectTimeout - setupClock.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                throw new TimeoutException();
            }

            offsets.Add(new TopicPartitionOffset(partition, consumer.QueryWatermarkOffsets(partition, remaining).High));
        }

        return offsets;
    }
}
