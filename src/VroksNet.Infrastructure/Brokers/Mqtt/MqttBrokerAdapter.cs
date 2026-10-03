using System.Text;
using MQTTnet;
using MQTTnet.Protocol;
using VroksNet.Application.Abstractions;
using VroksNet.Domain.Connections;
using VroksNet.Domain.TestScenarios;

namespace VroksNet.Infrastructure.Brokers.Mqtt;

/// <summary>
/// <see cref="ConnectionServiceType.Mqtt"/> (N2 of docs/broker-adapters-plan.md), through MQTTnet.
/// The connection value is <c>mqtt://[user:password@]host[:1883]</c>, or <c>mqtts://…[:8883]</c>
/// for TLS. The topic is the channel address.
/// <list type="bullet">
/// <item>Send publishes with the <c>qos</c> and <c>retain</c> options. At QoS 1/2 it waits for the
/// broker's acknowledgement; at QoS 0, which has none, it pings afterwards — the broker handles a
/// connection's packets in order, so the PINGRESP proves it has the message.</item>
/// <item>Listen is a plain subscription, which every subscriber gets its own copy of, so nothing is
/// taken from real consumers. Each parameter becomes "+", which stands for one "/"-separated
/// level, so a "."-separated channel with parameters can't be listened on. It subscribes with MQTT
/// 5's "don't send retained messages": a retained message is an old one, not the next. A message
/// still marked retained (an MQTT 3.1.1 broker) is skipped too.</item>
/// </list>
/// </summary>
public sealed class MqttBrokerAdapter : IListeningBrokerAdapter
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(10);

    public const string QosOption = "qos";
    public const string RetainOption = "retain";

    public ConnectionServiceType Type => ConnectionServiceType.Mqtt;

    public IReadOnlyList<BrokerOptionDefinition> Options { get; } =
    [
        new(
            QosOption,
            "QoS",
            SendDescription: "Delivery guarantee of the publish. Blank means 1 (at least once): the run waits for the broker's acknowledgement.",
            SendPlaceholder: "1 (default)",
            ListenDescription: "QoS of the temporary subscription. Blank means 1.",
            ListenPlaceholder: "1 (default)",
            AllowedValues: ["0", "1", "2"]),
        new(
            RetainOption,
            "Retain",
            SendDescription: "Publish as a retained message, so a subscriber that connects later still gets it. Blank means false.",
            SendPlaceholder: "false (default)",
            AllowedValues: ["true", "false"]),
    ];

    public async Task<ConnectionTestResult> TestAsync(Connection connection, CancellationToken cancellationToken)
    {
        if (ClientOptionsOf(connection.Value, TestTimeout) is not { } options)
        {
            return new ConnectionTestResult(false, InvalidConnectionStringMessage);
        }

        try
        {
            using var client = new MqttClientFactory().CreateMqttClient();
            await ConnectAsync(client, options, TestTimeout, cancellationToken);
            await client.DisconnectAsync(cancellationToken: CancellationToken.None);
            return new ConnectionTestResult(true, $"Connected to {new Uri(connection.Value.Trim()).Host}.");
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
        var topic = OperationCompatibility.ChannelAddressOf(operationKey);
        if (topic is null)
        {
            return new MessageSendResult(false, "This operation isn't AsyncAPI-shaped (expected \"channel:action\") — it can't be published to an Mqtt connection.");
        }

        if (ClientOptionsOf(connection.Value, SendTimeout) is not { } clientOptions)
        {
            return new MessageSendResult(false, InvalidConnectionStringMessage);
        }

        var qos = QosOf(options);
        var retain = options?[RetainOption] == "true";
        try
        {
            using var client = new MqttClientFactory().CreateMqttClient();
            await ConnectAsync(client, clientOptions, SendTimeout, cancellationToken);

            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(Encoding.UTF8.GetBytes(payload ?? string.Empty))
                .WithQualityOfServiceLevel(qos)
                .WithRetainFlag(retain)
                .Build();
            var result = await client.PublishAsync(message, cancellationToken).WaitAsync(SendTimeout, cancellationToken);
            if (!result.IsSuccess)
            {
                return new MessageSendResult(false, $"The broker refused the publish to topic \"{topic}\": {result.ReasonString ?? result.ReasonCode.ToString()}.");
            }

            if (qos == MqttQualityOfServiceLevel.AtMostOnce)
            {
                await client.PingAsync(cancellationToken).WaitAsync(SendTimeout, cancellationToken);
            }

            await client.DisconnectAsync(cancellationToken: CancellationToken.None);
            return new MessageSendResult(true, $"Published to topic \"{topic}\" (QoS {(int)qos}{(retain ? ", retained" : string.Empty)}).");
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
        if (TopicFilterOf(channel) is not { } topicFilter)
        {
            return new MessageListenResult(false, CantListenMessage(channel));
        }

        if (ClientOptionsOf(connection.Value, BrokerListening.ConnectTimeout) is not { } clientOptions)
        {
            return new MessageListenResult(false, InvalidConnectionStringMessage);
        }

        var stage = ListenStage.Connecting;
        try
        {
            using var client = new MqttClientFactory().CreateMqttClient();
            var received = new TaskCompletionSource<MqttApplicationMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            client.ApplicationMessageReceivedAsync += args =>
            {
                if (!args.ApplicationMessage.Retain)
                {
                    received.TrySetResult(args.ApplicationMessage);
                }

                return Task.CompletedTask;
            };

            await ConnectAsync(client, clientOptions, BrokerListening.ConnectTimeout, cancellationToken);
            stage = ListenStage.SettingUp;

            var subscription = new MqttClientSubscribeOptionsBuilder()
                .WithTopicFilter(filter => filter
                    .WithTopic(topicFilter)
                    .WithQualityOfServiceLevel(QosOf(options))
                    .WithRetainHandling(MqttRetainHandling.DoNotSendOnSubscribe))
                .Build();
            var subscribed = await client.SubscribeAsync(subscription, cancellationToken).WaitAsync(BrokerListening.ConnectTimeout, cancellationToken);
            if (subscribed.Items.FirstOrDefault() is { } item && (int)item.ResultCode > (int)MqttClientSubscribeResultCode.GrantedQoS2)
            {
                return new MessageListenResult(false, $"The broker refused the subscription to \"{topicFilter}\": {item.ResultCode}.");
            }

            stage = ListenStage.Listening;
            onListening?.Invoke();

            var message = await received.Task.WaitAsync(timeout, cancellationToken);
            await client.DisconnectAsync(cancellationToken: CancellationToken.None);
            return new MessageListenResult(true, $"Received on topic \"{message.Topic}\".", message.ConvertPayloadToString());
        }
        catch (Exception ex) when (ex is TimeoutException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return new MessageListenResult(false, BrokerListening.TimeoutMessage(stage, $"topic filter \"{topicFilter}\"", timeout));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new MessageListenResult(false, ex.Message);
        }
    }

    public string? WhyCantListen(ChannelPattern channel) => TopicFilterOf(channel) is null ? CantListenMessage(channel) : null;

    /// <summary>
    /// The topic filter for <paramref name="channel"/> ("devices/{id}/telemetry" → "devices/+/telemetry");
    /// null if it has parameters but isn't "/"-separated — an MQTT wildcard stands for one "/"-separated level.
    /// </summary>
    public static string? TopicFilterOf(ChannelPattern channel)
        => channel.HasParameters && channel.Separator != '/' ? null : channel.Render("+");

    /// <summary>The client options a connection value describes; null if it isn't an mqtt:// or mqtts:// URI with a host.</summary>
    public static MqttClientOptions? ClientOptionsOf(string value, TimeSpan timeout)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) || uri.Host.Length == 0
            || uri.Scheme is not ("mqtt" or "mqtts"))
        {
            return null;
        }

        var tls = uri.Scheme == "mqtts";
        var builder = new MqttClientOptionsBuilder()
            .WithTcpServer(uri.Host, uri.IsDefaultPort || uri.Port < 0 ? (tls ? 8883 : 1883) : uri.Port)
            .WithProtocolVersion(MQTTnet.Formatter.MqttProtocolVersion.V500)
            .WithClientId($"vroksnet-{Guid.NewGuid():N}")
            .WithCleanStart()
            .WithTimeout(timeout);
        if (tls)
        {
            builder.WithTlsOptions(options => options.UseTls());
        }

        if (uri.UserInfo.Length > 0)
        {
            var separatorIndex = uri.UserInfo.IndexOf(':');
            var user = Uri.UnescapeDataString(separatorIndex < 0 ? uri.UserInfo : uri.UserInfo[..separatorIndex]);
            var password = separatorIndex < 0 ? null : Uri.UnescapeDataString(uri.UserInfo[(separatorIndex + 1)..]);
            builder.WithCredentials(user, password);
        }

        return builder.Build();
    }

    private const string InvalidConnectionStringMessage = "Not a valid MQTT connection string (expected mqtt://[user:password@]host[:port], or mqtts://… for TLS).";

    private static MqttQualityOfServiceLevel QosOf(BrokerOptions? options) => options?[QosOption] switch
    {
        "0" => MqttQualityOfServiceLevel.AtMostOnce,
        "2" => MqttQualityOfServiceLevel.ExactlyOnce,
        _ => MqttQualityOfServiceLevel.AtLeastOnce
    };

    /// <exception cref="TimeoutException">The broker didn't accept the connection within <paramref name="timeout"/>.</exception>
    private static async Task ConnectAsync(IMqttClient client, MqttClientOptions options, TimeSpan timeout, CancellationToken cancellationToken)
    {
        // Cancelled, not abandoned, on timeout: the client stops trying instead of connecting later.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        try
        {
            var result = await client.ConnectAsync(options, timeoutCts.Token);
            if (result.ResultCode != MqttClientConnectResultCode.Success)
            {
                throw new InvalidOperationException($"The broker refused the connection: {result.ReasonString ?? result.ResultCode.ToString()}.");
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Timed out connecting to the broker after {timeout.TotalSeconds:0}s.");
        }
    }

    private static string CantListenMessage(ChannelPattern channel)
        => $"Channel \"{channel.Address}\" has parameters between \".\"-separated segments, and MQTT wildcards only stand for whole \"/\"-separated levels — it can't be listened on through an Mqtt connection.";
}
