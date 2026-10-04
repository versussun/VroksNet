using System.Text.RegularExpressions;
using Azure;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using VroksNet.Application.Abstractions;
using VroksNet.Domain.Connections;
using VroksNet.Domain.TestScenarios;

namespace VroksNet.Infrastructure.Brokers.ServiceBus;

/// <summary>
/// <see cref="ConnectionServiceType.ServiceBus"/> (N4 of docs/broker-adapters-plan.md), through
/// Azure.Messaging.ServiceBus. The connection value is a shared-access connection string
/// (<c>Endpoint=sb://…;SharedAccessKeyName=…;SharedAccessKey=…</c>, or <c>SharedAccessSignature=…</c>)
/// — what Aspire's <c>WithReference</c> hands out for the emulator, with
/// <c>UseDevelopmentEmulator=true</c>. An <c>EntityPath</c> in it limits the connection to that
/// queue or topic. The channel address is the queue or topic name.
/// <list type="bullet">
/// <item>Send sends one message to the queue or topic; the namespace has it once the send returns.</item>
/// <item>Listen works on topics only (ADR 0003): a queue has only competing consumers, so listening
/// on one would take messages from its real consumers. With the <c>subscription</c> option it reads
/// that existing subscription — one dedicated to VroksNet, since what Listen receives is gone from
/// it. Messages already waiting there when it starts (found by peeking, which takes nothing) are
/// skipped, per partition on a partitioned topic, so the message it reports is the next one. Give
/// that subscription a short default message time to live: nothing reads it between runs, and a
/// backlog too large to peek through in the setup budget fails every Listen.
/// Without it, it creates a temporary subscription for the run (which needs Manage rights) and
/// deletes it afterwards; one left behind deletes itself after <see cref="TemporarySubscriptionIdle"/>.
/// Entity names have no wildcards, so a channel with parameters can't be listened on.</item>
/// </list>
/// </summary>
public sealed class ServiceBusBrokerAdapter : IListeningBrokerAdapter
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(10);

    /// <summary>How long one receive waits before the next, while the listen window is open.</summary>
    private static readonly TimeSpan ReceiveWait = TimeSpan.FromSeconds(5);

    /// <summary>How many messages one peek or receive takes at most.</summary>
    private const int PeekBatch = 100;

    /// <summary>A temporary subscription that wasn't deleted (the process died) goes away after this long unused — Service Bus's minimum.</summary>
    public static readonly TimeSpan TemporarySubscriptionIdle = TimeSpan.FromMinutes(5);

    public const string SubscriptionOption = "subscription";

    public ConnectionServiceType Type => ConnectionServiceType.ServiceBus;

    public IReadOnlyList<BrokerOptionDefinition> Options { get; } =
    [
        new(
            SubscriptionOption,
            "Subscription",
            SendDescription: null,
            SendPlaceholder: null,
            ListenDescription: "An existing subscription of the topic, dedicated to VroksNet: Listen skips what's already waiting in it and takes the next message, removing what it reads. Blank means a temporary subscription for the run, which needs Manage rights on the connection.",
            ListenPlaceholder: "a temporary subscription (default)"),
    ];

    public async Task<ConnectionTestResult> TestAsync(Connection connection, CancellationToken cancellationToken)
    {
        if (ConnectionStringOf(connection.Value) is not { } connectionString)
        {
            return new ConnectionTestResult(false, InvalidConnectionStringMessage);
        }

        // Opening a send link reaches the namespace and checks the credentials without sending
        // anything. Without an EntityPath, a made-up entity is expected not to exist: the namespace
        // answering "not found" proves both.
        var entity = connectionString.EntityPath ?? $"vroksnet-connection-test-{Guid.NewGuid():N}";
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TestTimeout);
        try
        {
            await using var client = ClientOf(connection.Value, TestTimeout);
            await using var sender = client.CreateSender(entity);
            using var batch = await sender.CreateMessageBatchAsync(timeoutCts.Token);
            return new ConnectionTestResult(true, connectionString.EntityPath is null ? "Connected." : $"Connected — \"{entity}\" exists.");
        }
        catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessagingEntityNotFound && connectionString.EntityPath is null)
        {
            return new ConnectionTestResult(true, "Connected.");
        }
        catch (Exception ex) when (IsTimeout(ex, cancellationToken))
        {
            return new ConnectionTestResult(false, $"Timed out after {TestTimeout.TotalSeconds:0}s.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ConnectionTestResult(false, MessageOf(ex, $"\"{entity}\""));
        }
    }

    public async Task<MessageSendResult> SendAsync(Connection connection, string operationKey, string? payload, BrokerOptions? options, CancellationToken cancellationToken)
    {
        var address = OperationCompatibility.ChannelAddressOf(operationKey);
        if (address is null)
        {
            return new MessageSendResult(false, "This operation isn't AsyncAPI-shaped (expected \"channel:action\") — it can't be sent to an Azure Service Bus connection.");
        }

        if (ConnectionStringOf(connection.Value) is not { } connectionString)
        {
            return new MessageSendResult(false, InvalidConnectionStringMessage);
        }

        if (WrongEntityMessage(connectionString, address) is { } wrongEntity)
        {
            return new MessageSendResult(false, wrongEntity);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(SendTimeout);
        try
        {
            await using var client = ClientOf(connection.Value, SendTimeout);
            await using var sender = client.CreateSender(address);
            await sender.SendMessageAsync(new ServiceBusMessage(payload ?? string.Empty), timeoutCts.Token);
            return new MessageSendResult(true, $"Sent to \"{address}\".");
        }
        catch (Exception ex) when (IsTimeout(ex, cancellationToken))
        {
            return new MessageSendResult(false, $"Timed out after {SendTimeout.TotalSeconds:0}s.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new MessageSendResult(false, MessageOf(ex, $"queue or topic \"{address}\""));
        }
    }

    public async Task<MessageListenResult> ListenAsync(Connection connection, ChannelPattern channel, TimeSpan timeout, BrokerOptions? options, CancellationToken cancellationToken, Action? onListening = null)
    {
        if (WhyCantListen(channel, options) is { } reason)
        {
            return new MessageListenResult(false, reason);
        }

        if (ConnectionStringOf(connection.Value) is not { } connectionString)
        {
            return new MessageListenResult(false, InvalidConnectionStringMessage);
        }

        var topic = channel.Address;
        if (WrongEntityMessage(connectionString, topic) is { } wrongEntity)
        {
            return new MessageListenResult(false, wrongEntity);
        }

        var named = options?[SubscriptionOption];
        var subscription = named ?? $"vroksnet-{Guid.NewGuid():N}";
        var target = $"topic \"{topic}\"";
        var stage = ListenStage.Connecting;
        var created = false;
        ServiceBusAdministrationClient? administration = null;

        using var setupCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        setupCts.CancelAfter(BrokerListening.ConnectTimeout);
        try
        {
            await using var client = ClientOf(connection.Value, BrokerListening.ConnectTimeout);
            await using var receiver = client.CreateReceiver(topic, subscription, new ServiceBusReceiverOptions { ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete });
            var lastWaiting = new Dictionary<long, long>();
            if (named is null)
            {
                administration = AdministrationOf(connection.Value);
                await administration.CreateSubscriptionAsync(new CreateSubscriptionOptions(topic, subscription) { AutoDeleteOnIdle = TemporarySubscriptionIdle }, setupCts.Token);
                created = true;
                // Every message sent to the topic from now on is kept in the subscription until
                // read, so it's in place before the receiver's link is.
            }
            else
            {
                // Peeking takes nothing. It proves the namespace, the topic and the subscription,
                // and finds the last message already waiting in each partition: up to it, messages
                // are old and are skipped. (Receiving until the subscription is empty instead would
                // never end while the topic is busy.)
                while (await receiver.PeekMessagesAsync(PeekBatch, cancellationToken: setupCts.Token) is { Count: > 0 } waiting)
                {
                    stage = ListenStage.SettingUp;
                    foreach (var message in waiting)
                    {
                        RecordWaiting(lastWaiting, message.SequenceNumber);
                    }
                }
            }

            stage = ListenStage.Listening;
            onListening?.Invoke();

            using var listenCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            listenCts.CancelAfter(timeout);
            while (true)
            {
                var messages = await receiver.ReceiveMessagesAsync(PeekBatch, ReceiveWait, listenCts.Token);
                if (messages.FirstOrDefault(message => IsNew(lastWaiting, message.SequenceNumber)) is { } next)
                {
                    return new MessageListenResult(true, $"Received on {target} through subscription \"{subscription}\".", next.Body.ToString());
                }
            }
        }
        catch (Exception ex) when (named is not null && stage == ListenStage.SettingUp && IsTimeout(ex, cancellationToken))
        {
            // Still peeking at old messages: the next run would find even more of them.
            return new MessageListenResult(false, $"Subscription \"{named}\" of {target} holds more waiting messages than can be skipped in {BrokerListening.ConnectTimeout.TotalSeconds:0}s — purge it, give it a short default message time to live, or leave the broker option \"{SubscriptionOption}\" blank for a temporary subscription.");
        }
        catch (Exception ex) when (IsTimeout(ex, cancellationToken))
        {
            return new MessageListenResult(false, BrokerListening.TimeoutMessage(stage, target, timeout));
        }
        catch (Exception ex) when (named is null && !created && IsUnauthorized(ex))
        {
            return new MessageListenResult(false, $"The connection may not create a temporary subscription on {target} — grant it Manage rights, or name an existing subscription in the broker option \"{SubscriptionOption}\".");
        }
        catch (Exception ex) when (named is null && !created && ex is HttpRequestException or RequestFailedException { Status: 0 })
        {
            // No answer at all (status 0) from the management endpoint (HTTPS), which is separate
            // from messaging (AMQP); the Service Bus emulator doesn't serve it where its
            // connection string points.
            return new MessageListenResult(false, $"Couldn't reach the namespace's management endpoint to create a temporary subscription on {target} — name an existing subscription in the broker option \"{SubscriptionOption}\" (the Service Bus emulator needs one).");
        }
        catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessagingEntityNotFound && named is null && !created)
        {
            return new MessageListenResult(false, NoTopicMessage(topic));
        }
        catch (ServiceBusException ex) when (named is not null && stage == ListenStage.Connecting
            && ex.Reason is ServiceBusFailureReason.MessagingEntityNotFound or ServiceBusFailureReason.ServiceTimeout)
        {
            // The emulator closes the link to a missing subscription, which the client reports as a
            // timeout; Azure says "not found".
            return new MessageListenResult(false, $"Couldn't open subscription \"{named}\" of topic \"{topic}\" — check that both exist. If \"{topic}\" is a queue, it can only be sent to: Listen on a queue would take messages from its consumers.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new MessageListenResult(false, MessageOf(ex, target));
        }
        finally
        {
            if (created && administration is not null)
            {
                await DeleteQuietlyAsync(administration, topic, subscription);
            }
        }
    }

    public string? WhyCantListen(ChannelPattern channel, BrokerOptions? options)
        => channel.HasParameters
            ? $"Channel \"{channel.Address}\" has parameters, and an Azure Service Bus topic name has no wildcards — it can't be listened on through a ServiceBus connection."
            : null;

    /// <summary>
    /// The partition a sequence number belongs to: its top 16 bits on a partitioned entity (0 on
    /// any other). Sequence numbers only grow within one partition, so they're compared per partition.
    /// </summary>
    public static long PartitionOf(long sequenceNumber) => sequenceNumber >> 48;

    /// <summary>Records <paramref name="sequenceNumber"/> as waiting before Listen started: the highest per partition.</summary>
    public static void RecordWaiting(Dictionary<long, long> lastWaiting, long sequenceNumber)
    {
        var partition = PartitionOf(sequenceNumber);
        if (!lastWaiting.TryGetValue(partition, out var last) || sequenceNumber > last)
        {
            lastWaiting[partition] = sequenceNumber;
        }
    }

    /// <summary>Whether a message came after everything that was waiting in its partition when Listen started.</summary>
    public static bool IsNew(IReadOnlyDictionary<long, long> lastWaiting, long sequenceNumber)
        => !lastWaiting.TryGetValue(PartitionOf(sequenceNumber), out var last) || sequenceNumber > last;

    /// <summary>
    /// The parsed connection string; null unless it has an endpoint and either a shared access key
    /// (name and key) or a shared access signature — the client takes nothing else from a string.
    /// </summary>
    public static ServiceBusConnectionStringProperties? ConnectionStringOf(string value)
    {
        try
        {
            var properties = ServiceBusConnectionStringProperties.Parse(value.Trim());
            var hasKey = !string.IsNullOrEmpty(properties.SharedAccessKeyName) && !string.IsNullOrEmpty(properties.SharedAccessKey);
            return properties.Endpoint is not null && (hasKey || !string.IsNullOrEmpty(properties.SharedAccessSignature)) ? properties : null;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return null;
        }
    }

    private const string InvalidConnectionStringMessage = "Not a valid Azure Service Bus connection string (expected Endpoint=sb://namespace.servicebus.windows.net/;SharedAccessKeyName=…;SharedAccessKey=…). Microsoft Entra ID sign-in (a namespace name alone) isn't supported.";

    private static ServiceBusClient ClientOf(string connectionString, TimeSpan timeout)
        => new(connectionString.Trim(), new ServiceBusClientOptions
        {
            // No retrying in the background: the adapter's own budget decides, and a failure is
            // reported instead of hidden behind retries.
            RetryOptions = new ServiceBusRetryOptions { MaxRetries = 0, TryTimeout = timeout },
        });

    private static ServiceBusAdministrationClient AdministrationOf(string connectionString)
    {
        var options = new ServiceBusAdministrationClientOptions();
        options.Retry.MaxRetries = 0;
        options.Retry.NetworkTimeout = BrokerListening.ConnectTimeout;
        return new ServiceBusAdministrationClient(connectionString.Trim(), options);
    }

    /// <summary>A connection string with an EntityPath only reaches that entity; the client would throw on any other.</summary>
    private static string? WrongEntityMessage(ServiceBusConnectionStringProperties connectionString, string entity)
        => connectionString.EntityPath is { } entityPath && !string.Equals(entityPath, entity, StringComparison.OrdinalIgnoreCase)
            ? $"The connection string is limited to \"{entityPath}\" (its EntityPath), but the channel is \"{entity}\"."
            : null;

    private static string NoTopicMessage(string topic)
        => $"No topic \"{topic}\" in the namespace — if it's a queue, it can only be sent to: Listen on a queue would take messages from its consumers.";

    /// <summary>Deletes the run's temporary subscription; a failure is left to its auto-delete.</summary>
    private static async Task DeleteQuietlyAsync(ServiceBusAdministrationClient administration, string topic, string subscription)
    {
        try
        {
            using var deleteCts = new CancellationTokenSource(BrokerListening.ConnectTimeout);
            await administration.DeleteSubscriptionAsync(topic, subscription, deleteCts.Token);
        }
        catch (Exception)
        {
            // AutoDeleteOnIdle removes it after TemporarySubscriptionIdle.
        }
    }

    /// <summary>The adapter's own budget ran out (not the caller's cancellation), or the client gave up waiting.</summary>
    private static bool IsTimeout(Exception exception, CancellationToken cancellationToken)
        => (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
            || exception is TimeoutException;

    private static bool IsUnauthorized(Exception exception)
        => exception is UnauthorizedAccessException || exception is RequestFailedException { Status: 401 or 403 };

    /// <summary>
    /// A message safe to show: the client's own messages carry tracking ids, timestamps and a
    /// troubleshooting link, so only their first part is kept.
    /// </summary>
    private static string MessageOf(Exception exception, string what) => exception switch
    {
        UnauthorizedAccessException or RequestFailedException { Status: 401 or 403 } => "The namespace refused the credentials, or they lack the rights for this.",
        ServiceBusException { Reason: ServiceBusFailureReason.MessagingEntityNotFound } => $"No {what} in the namespace.",
        ServiceBusException { Reason: ServiceBusFailureReason.MessagingEntityDisabled } => $"The {what} is disabled.",
        ServiceBusException { Reason: ServiceBusFailureReason.ServiceCommunicationProblem } => "Couldn't reach the namespace.",
        ServiceBusException { Reason: ServiceBusFailureReason.ServiceTimeout } => "The namespace didn't answer in time.",
        ServiceBusException { Reason: ServiceBusFailureReason.ServiceBusy } => "The namespace is busy — try again later.",
        ServiceBusException { Reason: ServiceBusFailureReason.QuotaExceeded } => $"The {what} is full (quota exceeded).",
        ServiceBusException { Reason: ServiceBusFailureReason.MessageSizeExceeded } => "The message is larger than the namespace allows.",
        _ => FirstPartOf(exception.Message)
    };

    private static string FirstPartOf(string message)
        => Regex.Split(message, @"\s*(?:\(\w+\)\.\s*)?(?:For troubleshooting|Reference:|TrackingId:|Status:)", RegexOptions.CultureInvariant)[0].Trim();
}
