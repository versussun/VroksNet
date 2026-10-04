using System.Globalization;
using System.Text.Json;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using VroksNet.Application.Abstractions;
using VroksNet.Domain.Connections;
using VroksNet.Domain.TestScenarios;

namespace VroksNet.Infrastructure.Brokers.Aws;

/// <summary>
/// <see cref="ConnectionServiceType.Sns"/> (N5 of docs/broker-adapters-plan.md), through
/// AWSSDK.SimpleNotificationService and AWSSDK.SQS. The connection value is an
/// <see cref="AwsConnectionValue"/>; the channel address is the topic's name (in the credentials'
/// own account and the value's region) or ARN.
/// <list type="bullet">
/// <item>Send publishes to the topic; AWS has the message once Publish returns its id.</item>
/// <item>Listen reads an SQS queue subscribed to the topic (ADR 0003) — every subscription gets its
/// own copy, so nothing is taken from the topic's real subscribers. Without the <c>queue</c> option
/// it creates a temporary queue, subscribes it with raw delivery, and deletes both afterwards
/// (which needs the SQS and SNS permissions for that). With it, it reads that existing queue, which
/// must be subscribed to the topic and dedicated to VroksNet, since what Listen reads is gone from
/// it. SQS keeps no order, so it first sends a marker to the queue: messages SQS got before the
/// marker (by SQS's own timestamps) are old and are removed unread. Topic names have no
/// wildcards, so a channel with parameters can't be listened on.</item>
/// </list>
/// </summary>
public sealed class SnsBrokerAdapter : IListeningBrokerAdapter
{
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Per request while listening: an SQS long poll waits up to 20s, so the request needs longer.</summary>
    private static readonly TimeSpan ReceiveRequestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>A named queue holding more unread old messages than this fails the run rather than keep them all in memory.</summary>
    private const int MaxPendingMessages = 10_000;

    public const string QueueOption = "queue";

    /// <summary>The message attribute that marks the marker Listen sends to a named queue.</summary>
    public const string MarkerAttribute = "vroksnet-listen-marker";

    public ConnectionServiceType Type => ConnectionServiceType.Sns;

    public IReadOnlyList<BrokerOptionDefinition> Options { get; } =
    [
        AwsClients.MessageGroupIdDefinition("topic"),
        new(
            QueueOption,
            "SQS queue",
            SendDescription: null,
            SendPlaceholder: null,
            ListenDescription: "An existing SQS queue (name or URL) subscribed to the topic, dedicated to VroksNet: Listen removes the messages already in it and takes the next one. Blank means a temporary queue for the run, which needs permission to create SQS queues and SNS subscriptions.",
            ListenPlaceholder: "a temporary queue (default)"),
    ];

    public Task<ConnectionTestResult> TestAsync(Connection connection, CancellationToken cancellationToken)
        => AwsClients.TestAsync(connection.Value, cancellationToken);

    public async Task<MessageSendResult> SendAsync(Connection connection, string operationKey, string? payload, BrokerOptions? options, CancellationToken cancellationToken)
    {
        var address = OperationCompatibility.ChannelAddressOf(operationKey);
        if (address is null)
        {
            return new MessageSendResult(false, "This operation isn't AsyncAPI-shaped (expected \"channel:action\") — it can't be published to an SNS connection.");
        }

        if (AwsConnectionValue.Parse(connection.Value) is not { } value)
        {
            return new MessageSendResult(false, AwsConnectionValue.InvalidMessage);
        }

        if (string.IsNullOrEmpty(payload))
        {
            return new MessageSendResult(false, "SNS can't publish an empty message — the operation has no example to send.");
        }

        var topic = AwsClients.NameOf(address);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(SendTimeout);
        try
        {
            using var sns = AwsClients.Sns(value, SendTimeout);
            var request = new PublishRequest { TopicArn = await AwsClients.TopicArnAsync(value, address, SendTimeout, timeoutCts.Token), Message = payload };
            if (AwsClients.IsFifo(address))
            {
                request.MessageGroupId = options?[AwsClients.MessageGroupIdOption] ?? AwsClients.DefaultMessageGroupId;
                request.MessageDeduplicationId = Guid.NewGuid().ToString("N");
            }

            var published = await sns.PublishAsync(request, timeoutCts.Token);
            return new MessageSendResult(true, $"Published message {published.MessageId} to topic \"{topic}\".");
        }
        catch (Exception ex) when (AwsClients.IsTimeout(ex, cancellationToken))
        {
            return new MessageSendResult(false, $"Timed out after {SendTimeout.TotalSeconds:0}s.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new MessageSendResult(false, AwsClients.MessageOf(ex, $"topic \"{topic}\""));
        }
    }

    public async Task<MessageListenResult> ListenAsync(Connection connection, ChannelPattern channel, TimeSpan timeout, BrokerOptions? options, CancellationToken cancellationToken, Action? onListening = null)
    {
        if (WhyCantListen(channel, options) is { } reason)
        {
            return new MessageListenResult(false, reason);
        }

        if (AwsConnectionValue.Parse(connection.Value) is not { } value)
        {
            return new MessageListenResult(false, AwsConnectionValue.InvalidMessage);
        }

        var named = options?[QueueOption];
        var target = $"topic \"{AwsClients.NameOf(channel.Address)}\"";
        var stage = ListenStage.Connecting;
        string? temporaryQueueUrl = null;
        string? subscriptionArn = null;

        using var setupCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        setupCts.CancelAfter(BrokerListening.ConnectTimeout);
        using var sqs = AwsClients.Sqs(value, ReceiveRequestTimeout);
        using var sns = AwsClients.Sns(value, BrokerListening.ConnectTimeout);
        try
        {
            var topicArn = await AwsClients.TopicArnAsync(value, channel.Address, BrokerListening.ConnectTimeout, setupCts.Token);
            stage = ListenStage.SettingUp;
            if (named is null)
            {
                temporaryQueueUrl = await CreateTemporaryQueueAsync(sqs, channel.Address, topicArn, setupCts.Token);
                subscriptionArn = await SubscribeAsync(sns, sqs, temporaryQueueUrl, topicArn, setupCts.Token);
                stage = ListenStage.Listening;
                onListening?.Invoke();
                return await ReceiveFirstAsync(sqs, temporaryQueueUrl, target, timeout, cancellationToken);
            }

            var queueUrl = await AwsClients.QueueUrlAsync(sqs, named, setupCts.Token);
            var marker = Guid.NewGuid().ToString("N");
            await SendMarkerAsync(sqs, queueUrl, named, marker, setupCts.Token);
            stage = ListenStage.Listening;
            onListening?.Invoke();
            return await ReceiveAfterMarkerAsync(sqs, queueUrl, topicArn, marker, target, timeout, cancellationToken);
        }
        catch (Exception ex) when (AwsClients.IsTimeout(ex, cancellationToken))
        {
            return new MessageListenResult(false, BrokerListening.TimeoutMessage(stage, target, timeout));
        }
        catch (Exception ex) when (named is null && stage == ListenStage.SettingUp && AwsClients.IsAccessDenied(ex))
        {
            return new MessageListenResult(false, $"The credentials may not set up a temporary queue for {target} ({ex.Message}) — grant them the SQS and SNS permissions for that, or name an existing queue subscribed to the topic in the broker option \"{QueueOption}\".");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var what = stage == ListenStage.Connecting || named is null ? target : $"queue \"{AwsClients.NameOf(named)}\"";
            return new MessageListenResult(false, AwsClients.MessageOf(ex, what));
        }
        finally
        {
            await CleanUpQuietlyAsync(sqs, sns, temporaryQueueUrl, subscriptionArn);
        }
    }

    public string? WhyCantListen(ChannelPattern channel, BrokerOptions? options)
        => channel.HasParameters
            ? $"Channel \"{channel.Address}\" has parameters, and an SNS topic name has no wildcards — it can't be listened on through an Sns connection."
            : null;

    /// <summary>
    /// The message an SQS message carries: with raw delivery its body; otherwise SNS's JSON envelope
    /// around it, whose "Message" it is. Null for an envelope from another topic than <paramref name="topicArn"/>.
    /// </summary>
    public static string? PayloadOf(string body, string topicArn)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("Type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "Notification"
                && root.TryGetProperty("Message", out var message) && message.ValueKind == JsonValueKind.String)
            {
                return root.TryGetProperty("TopicArn", out var from) && from.ValueKind == JsonValueKind.String && from.GetString() != topicArn
                    ? null
                    : message.GetString();
            }
        }
        catch (JsonException)
        {
            // Not JSON, so not an envelope: the body is the message.
        }

        return body;
    }

    private static async Task<string> CreateTemporaryQueueAsync(IAmazonSQS sqs, string topicAddress, string topicArn, CancellationToken cancellationToken)
    {
        // A FIFO topic only delivers to FIFO queues.
        var fifo = AwsClients.IsFifo(topicAddress);
        var request = new CreateQueueRequest
        {
            QueueName = $"vroksnet-{Guid.NewGuid():N}{(fifo ? ".fifo" : string.Empty)}",
            Attributes = new Dictionary<string, string> { ["MessageRetentionPeriod"] = "300" },
            // So a queue left behind (the process died mid-run) can be found and deleted.
            Tags = new Dictionary<string, string> { ["vroksnet"] = "temporary-listen-queue" },
        };
        if (fifo)
        {
            request.Attributes["FifoQueue"] = "true";
        }

        var queueUrl = (await sqs.CreateQueueAsync(request, cancellationToken)).QueueUrl;
        var queueArn = (await sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest { QueueUrl = queueUrl, AttributeNames = ["QueueArn"] }, cancellationToken)).Attributes["QueueArn"];
        var policy = JsonSerializer.Serialize(new
        {
            Version = "2012-10-17",
            Statement = new[]
            {
                new
                {
                    Effect = "Allow",
                    Principal = new { Service = "sns.amazonaws.com" },
                    Action = "sqs:SendMessage",
                    Resource = queueArn,
                    Condition = new { ArnEquals = new Dictionary<string, string> { ["aws:SourceArn"] = topicArn } },
                },
            },
        });
        await sqs.SetQueueAttributesAsync(new SetQueueAttributesRequest { QueueUrl = queueUrl, Attributes = new Dictionary<string, string> { ["Policy"] = policy } }, cancellationToken);
        return queueUrl;
    }

    private static async Task<string> SubscribeAsync(IAmazonSimpleNotificationService sns, IAmazonSQS sqs, string queueUrl, string topicArn, CancellationToken cancellationToken)
    {
        var queueArn = (await sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest { QueueUrl = queueUrl, AttributeNames = ["QueueArn"] }, cancellationToken)).Attributes["QueueArn"];
        var subscription = await sns.SubscribeAsync(new SubscribeRequest
        {
            TopicArn = topicArn,
            Protocol = "sqs",
            Endpoint = queueArn,
            Attributes = new Dictionary<string, string> { ["RawMessageDelivery"] = "true" },
            ReturnSubscriptionArn = true,
        }, cancellationToken);
        // A queue in the topic's own account is subscribed at once; across accounts SNS waits for
        // a confirmation, which this run won't get.
        return subscription.SubscriptionArn.StartsWith("arn:", StringComparison.Ordinal)
            ? subscription.SubscriptionArn
            : throw new InvalidOperationException("SNS is waiting for the subscription to be confirmed (the topic is in another account) — name a queue already subscribed to it in the broker option \"queue\".");
    }

    private static async Task SendMarkerAsync(IAmazonSQS sqs, string queueUrl, string queue, string marker, CancellationToken cancellationToken)
    {
        var request = new SendMessageRequest
        {
            QueueUrl = queueUrl,
            MessageBody = "VroksNet Listen marker — safe to delete.",
            MessageAttributes = new Dictionary<string, Amazon.SQS.Model.MessageAttributeValue> { [MarkerAttribute] = new() { DataType = "String", StringValue = marker } },
        };
        if (AwsClients.IsFifo(queue))
        {
            request.MessageGroupId = AwsClients.DefaultMessageGroupId;
            request.MessageDeduplicationId = marker;
        }

        await sqs.SendMessageAsync(request, cancellationToken);
    }

    /// <summary>The temporary queue only ever gets messages published after it was subscribed, so its first one is the answer.</summary>
    private static async Task<MessageListenResult> ReceiveFirstAsync(IAmazonSQS sqs, string queueUrl, string target, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var listenCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        listenCts.CancelAfter(timeout);
        while (true)
        {
            var messages = await ReceiveAsync(sqs, queueUrl, timeout, listenCts.Token);
            if (messages.Count > 0)
            {
                return new MessageListenResult(true, $"Received on {target} through a temporary queue.", messages[0].Body);
            }
        }
    }

    /// <summary>
    /// Reads the named queue until a message SQS got after the marker shows up. Until the marker
    /// itself has been read, messages can't be told apart, so they're held (hidden from other
    /// readers by their visibility timeout); once it's read, the older ones are deleted.
    /// </summary>
    private static async Task<MessageListenResult> ReceiveAfterMarkerAsync(IAmazonSQS sqs, string queueUrl, string topicArn, string marker, string target, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var listenCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        listenCts.CancelAfter(timeout);
        var pending = new Dictionary<string, Message>();
        long? markerSentAt = null;
        while (true)
        {
            var old = new List<Message>();
            foreach (var message in await ReceiveAsync(sqs, queueUrl, timeout, listenCts.Token))
            {
                if (message.MessageAttributes?.GetValueOrDefault(MarkerAttribute)?.StringValue == marker)
                {
                    markerSentAt = SentAt(message);
                    old.Add(message);
                }
                else
                {
                    pending.TryAdd(message.MessageId, message);
                }
            }

            if (markerSentAt is { } boundary)
            {
                old.AddRange(pending.Values.Where(message => SentAt(message) < boundary));
                var next = pending.Values
                    .Where(message => SentAt(message) >= boundary)
                    .Select(message => (Message: message, Payload: PayloadOf(message.Body, topicArn)))
                    .OrderBy(candidate => SentAt(candidate.Message))
                    .ToList();
                // An envelope from another topic subscribed to the same queue isn't this run's.
                old.AddRange(next.Where(candidate => candidate.Payload is null).Select(candidate => candidate.Message));
                var found = next.FirstOrDefault(candidate => candidate.Payload is not null);
                if (found.Message is not null)
                {
                    old.Add(found.Message);
                    await DeleteQuietlyAsync(sqs, queueUrl, old);
                    return new MessageListenResult(true, $"Received on {target} through queue \"{AwsClients.NameOf(queueUrl)}\".", found.Payload);
                }

                foreach (var message in old)
                {
                    pending.Remove(message.MessageId);
                }
            }

            await DeleteQuietlyAsync(sqs, queueUrl, old);
            if (pending.Count > MaxPendingMessages)
            {
                return new MessageListenResult(false, $"Queue \"{AwsClients.NameOf(queueUrl)}\" holds more than {MaxPendingMessages} old messages — purge it (and give it a short message retention period), or leave the broker option \"{QueueOption}\" blank for a temporary queue.");
            }
        }
    }

    private static async Task<List<Message>> ReceiveAsync(IAmazonSQS sqs, string queueUrl, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var response = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest
        {
            QueueUrl = queueUrl,
            MaxNumberOfMessages = 10,
            WaitTimeSeconds = 20,
            // Held messages stay hidden for the whole run, so they aren't read twice.
            VisibilityTimeout = (int)Math.Min(43_200, timeout.TotalSeconds + 60),
            MessageSystemAttributeNames = ["SentTimestamp"],
            MessageAttributeNames = [MarkerAttribute],
        }, cancellationToken);
        return response.Messages ?? [];
    }

    /// <summary>When SQS got the message, in milliseconds since the epoch (SQS's own clock).</summary>
    private static long SentAt(Message message)
        => message.Attributes is { } attributes && attributes.TryGetValue("SentTimestamp", out var sentAt) && long.TryParse(sentAt, NumberStyles.None, CultureInfo.InvariantCulture, out var milliseconds)
            ? milliseconds
            : 0;

    private static async Task DeleteQuietlyAsync(IAmazonSQS sqs, string queueUrl, List<Message> messages)
    {
        foreach (var batch in messages.Chunk(10))
        {
            try
            {
                using var deleteCts = new CancellationTokenSource(BrokerListening.ConnectTimeout);
                await sqs.DeleteMessageBatchAsync(new DeleteMessageBatchRequest
                {
                    QueueUrl = queueUrl,
                    Entries = batch.Select((message, index) => new DeleteMessageBatchRequestEntry { Id = index.ToString(CultureInfo.InvariantCulture), ReceiptHandle = message.ReceiptHandle }).ToList(),
                }, deleteCts.Token);
            }
            catch (Exception)
            {
                // Left in the queue, they're older than the next run's marker and are deleted then.
            }
        }
    }

    /// <summary>Removes the run's temporary subscription and queue; failures are left behind (the queue is tagged "vroksnet").</summary>
    private static async Task CleanUpQuietlyAsync(IAmazonSQS sqs, IAmazonSimpleNotificationService sns, string? queueUrl, string? subscriptionArn)
    {
        using var cleanUpCts = new CancellationTokenSource(BrokerListening.ConnectTimeout);
        if (subscriptionArn is not null)
        {
            try
            {
                await sns.UnsubscribeAsync(subscriptionArn, cleanUpCts.Token);
            }
            catch (Exception)
            {
                // Deleting the queue below leaves the subscription with nowhere to deliver.
            }
        }

        if (queueUrl is not null)
        {
            try
            {
                await sqs.DeleteQueueAsync(queueUrl, cleanUpCts.Token);
            }
            catch (Exception)
            {
                // Tagged "vroksnet", so it can be found and deleted by hand.
            }
        }
    }
}
