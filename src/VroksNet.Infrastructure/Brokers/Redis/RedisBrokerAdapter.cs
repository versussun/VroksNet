using System.Text.Json;
using System.Text.RegularExpressions;
using StackExchange.Redis;
using VroksNet.Application.Abstractions;
using VroksNet.Domain.Connections;
using VroksNet.Domain.TestScenarios;

namespace VroksNet.Infrastructure.Brokers.Redis;

/// <summary>
/// <see cref="ConnectionServiceType.Redis"/> (N3 of docs/broker-adapters-plan.md), through
/// StackExchange.Redis. The connection value is a StackExchange.Redis configuration string
/// (<c>host:6379,password=…,ssl=true</c> — what Aspire's <c>WithReference</c> hands out) or a
/// <c>redis://[user:password@]host[:6379][/db]</c> URI (<c>rediss://</c> for TLS). The channel
/// address is the Pub/Sub channel or the stream key, depending on the <c>mode</c> option.
/// <list type="bullet">
/// <item><c>pubsub</c> (the default): Send is a PUBLISH, which the server has once it replies with
/// the number of receivers. Listen is a plain subscription — every subscriber gets its own copy —
/// with each parameter as "*" in a PSUBSCRIBE glob. A glob "*" also matches separators, so a
/// message on a channel the pattern doesn't strictly match is skipped.</item>
/// <item><c>stream</c>: Send is an XADD with the payload in a <c>payload</c> field. Listen reads
/// the stream after its last entry (XREAD from "$", without a consumer group, so real consumer
/// groups don't notice), polling since StackExchange.Redis doesn't block. A stream is one key,
/// so a channel with parameters can't be listened on in this mode.</item>
/// </list>
/// </summary>
public sealed class RedisBrokerAdapter : IListeningBrokerAdapter
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StreamPollInterval = TimeSpan.FromMilliseconds(100);

    public const string ModeOption = "mode";
    public const string PubSubMode = "pubsub";
    public const string StreamMode = "stream";

    /// <summary>The stream entry field Send writes the payload to, and Listen reads it from first.</summary>
    public const string PayloadField = "payload";

    public ConnectionServiceType Type => ConnectionServiceType.Redis;

    public IReadOnlyList<BrokerOptionDefinition> Options { get; } =
    [
        new(
            ModeOption,
            "Mode",
            SendDescription: $"pubsub publishes to the channel; stream adds an entry to the stream with that key, the payload in its \"{PayloadField}\" field. Blank means pubsub.",
            SendPlaceholder: "pubsub (default)",
            ListenDescription: "pubsub subscribes to the channel; stream waits for the next entry added to the stream with that key, without a consumer group. Blank means pubsub.",
            ListenPlaceholder: "pubsub (default)",
            AllowedValues: [PubSubMode, StreamMode]),
    ];

    public async Task<ConnectionTestResult> TestAsync(Connection connection, CancellationToken cancellationToken)
    {
        if (ConfigurationOf(connection.Value, TestTimeout) is not { } configuration)
        {
            return new ConnectionTestResult(false, InvalidConnectionStringMessage);
        }

        try
        {
            await using var multiplexer = await ConnectAsync(configuration, cancellationToken);
            var roundTrip = await multiplexer.GetDatabase().PingAsync().WaitAsync(TestTimeout, cancellationToken);
            return new ConnectionTestResult(true, $"Connected — round trip {roundTrip.TotalMilliseconds:0}ms.");
        }
        catch (TimeoutException)
        {
            return new ConnectionTestResult(false, $"Timed out after {TestTimeout.TotalSeconds:0}s.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ConnectionTestResult(false, MessageOf(ex));
        }
    }

    public async Task<MessageSendResult> SendAsync(Connection connection, string operationKey, string? payload, BrokerOptions? options, CancellationToken cancellationToken)
    {
        var address = OperationCompatibility.ChannelAddressOf(operationKey);
        if (address is null)
        {
            return new MessageSendResult(false, "This operation isn't AsyncAPI-shaped (expected \"channel:action\") — it can't be published to a Redis connection.");
        }

        if (ConfigurationOf(connection.Value, SendTimeout) is not { } configuration)
        {
            return new MessageSendResult(false, InvalidConnectionStringMessage);
        }

        try
        {
            await using var multiplexer = await ConnectAsync(configuration, cancellationToken);
            if (IsStreamMode(options))
            {
                var id = await multiplexer.GetDatabase().StreamAddAsync(address, PayloadField, payload ?? string.Empty).WaitAsync(SendTimeout, cancellationToken);
                return new MessageSendResult(true, $"Added entry {id} to stream \"{address}\".");
            }

            var receivers = await multiplexer.GetSubscriber().PublishAsync(RedisChannel.Literal(address), payload ?? string.Empty).WaitAsync(SendTimeout, cancellationToken);
            return new MessageSendResult(true, $"Published to channel \"{address}\" ({receivers} {(receivers == 1 ? "subscriber" : "subscribers")} received it).");
        }
        catch (TimeoutException)
        {
            return new MessageSendResult(false, $"Timed out after {SendTimeout.TotalSeconds:0}s.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new MessageSendResult(false, MessageOf(ex));
        }
    }

    public async Task<MessageListenResult> ListenAsync(Connection connection, ChannelPattern channel, TimeSpan timeout, BrokerOptions? options, CancellationToken cancellationToken, Action? onListening = null)
    {
        if (WhyCantListen(channel, options) is { } reason)
        {
            return new MessageListenResult(false, reason);
        }

        if (ConfigurationOf(connection.Value, BrokerListening.ConnectTimeout) is not { } configuration)
        {
            return new MessageListenResult(false, InvalidConnectionStringMessage);
        }

        var stream = IsStreamMode(options);
        var target = stream ? $"stream \"{channel.Address}\"" : $"channel \"{SubscriptionOf(channel)}\"";
        var stage = ListenStage.Connecting;
        try
        {
            await using var multiplexer = await ConnectAsync(configuration, cancellationToken);
            stage = ListenStage.SettingUp;

            using var setupCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            setupCts.CancelAfter(BrokerListening.ConnectTimeout);
            return stream
                ? await ListenToStreamAsync(multiplexer.GetDatabase(), channel.Address, timeout, setupCts.Token, cancellationToken, () =>
                {
                    stage = ListenStage.Listening;
                    onListening?.Invoke();
                })
                : await ListenToChannelAsync(multiplexer.GetSubscriber(), channel, timeout, setupCts.Token, cancellationToken, () =>
                {
                    stage = ListenStage.Listening;
                    onListening?.Invoke();
                });
        }
        catch (Exception ex) when (ex is TimeoutException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return new MessageListenResult(false, BrokerListening.TimeoutMessage(stage, target, timeout));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new MessageListenResult(false, MessageOf(ex));
        }
    }

    public string? WhyCantListen(ChannelPattern channel, BrokerOptions? options)
        => IsStreamMode(options) && channel.HasParameters
            ? $"Channel \"{channel.Address}\" has parameters, and a Redis stream is one key — it can't be listened on in stream mode. Use pubsub mode, or a channel without parameters."
            : null;

    /// <summary>
    /// What to subscribe to for <paramref name="channel"/>: the address itself without parameters,
    /// otherwise a PSUBSCRIBE glob with each parameter as "*" and glob characters in the literal
    /// segments escaped ("orders.{region}.created" → "orders.*.created").
    /// </summary>
    public static RedisChannel SubscriptionOf(ChannelPattern channel)
        => channel.HasParameters
            ? RedisChannel.Pattern(string.Join(channel.Separator, channel.Segments.Select(segment => segment.IsParameter ? "*" : EscapeGlob(segment.Text))))
            : RedisChannel.Literal(channel.Address);

    /// <summary>Whether <paramref name="channelName"/> strictly matches <paramref name="channel"/>: each parameter stands for exactly one non-empty segment.</summary>
    public static bool Matches(ChannelPattern channel, string channelName)
    {
        var separator = Regex.Escape(channel.Separator.ToString());
        var pattern = string.Join(separator, channel.Segments.Select(segment => segment.IsParameter ? $"[^{separator}]+" : Regex.Escape(segment.Text)));
        return Regex.IsMatch(channelName, $"^{pattern}$", RegexOptions.CultureInvariant);
    }

    /// <summary>
    /// The client configuration a connection value describes, with this adapter's timeouts and no
    /// retrying in the background; null if it's neither a StackExchange.Redis configuration string
    /// nor a redis:// or rediss:// URI with a host.
    /// </summary>
    public static ConfigurationOptions? ConfigurationOf(string value, TimeSpan timeout)
    {
        var trimmed = value.Trim();
        var configuration = trimmed.Contains("://", StringComparison.Ordinal) ? ConfigurationOfUri(trimmed) : ConfigurationOfString(trimmed);
        if (configuration is null)
        {
            return null;
        }

        var milliseconds = (int)timeout.TotalMilliseconds;
        configuration.AbortOnConnectFail = true;
        configuration.ConnectRetry = 1;
        configuration.ConnectTimeout = milliseconds;
        configuration.SyncTimeout = milliseconds;
        configuration.AsyncTimeout = milliseconds;
        configuration.ClientName = "vroksnet";
        return configuration;
    }

    private static ConfigurationOptions? ConfigurationOfString(string value)
    {
        try
        {
            var configuration = ConfigurationOptions.Parse(value);
            return configuration.EndPoints.Count == 0 ? null : configuration;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static ConfigurationOptions? ConfigurationOfUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Host.Length == 0 || uri.Scheme is not ("redis" or "rediss"))
        {
            return null;
        }

        var configuration = new ConfigurationOptions { Ssl = uri.Scheme == "rediss" };
        configuration.EndPoints.Add(uri.Host, uri.IsDefaultPort || uri.Port < 0 ? 6379 : uri.Port);

        var separatorIndex = uri.UserInfo.IndexOf(':');
        if (separatorIndex >= 0)
        {
            var user = Uri.UnescapeDataString(uri.UserInfo[..separatorIndex]);
            configuration.User = user.Length == 0 ? null : user;
            configuration.Password = Uri.UnescapeDataString(uri.UserInfo[(separatorIndex + 1)..]);
        }
        else if (uri.UserInfo.Length > 0)
        {
            // redis://secret@host: a lone user-info part is the password, as in redis-cli.
            configuration.Password = Uri.UnescapeDataString(uri.UserInfo);
        }

        var database = uri.AbsolutePath.Trim('/');
        if (database.Length == 0)
        {
            return configuration;
        }

        if (!int.TryParse(database, out var databaseNumber) || databaseNumber < 0)
        {
            return null;
        }

        configuration.DefaultDatabase = databaseNumber;
        return configuration;
    }

    /// <summary>
    /// The payload of a stream entry: its <see cref="PayloadField"/> if it has one, else its only
    /// field's value, else all its fields as a JSON object (the last value wins for a repeated field).
    /// </summary>
    public static string PayloadOf(NameValueEntry[] fields)
    {
        if (fields.FirstOrDefault(field => field.Name == PayloadField) is { Name.IsNull: false } payload)
        {
            return payload.Value.ToString();
        }

        if (fields.Length == 1)
        {
            return fields[0].Value.ToString();
        }

        var values = new Dictionary<string, string>();
        foreach (var field in fields)
        {
            values[field.Name.ToString()] = field.Value.ToString();
        }

        return JsonSerializer.Serialize(values);
    }

    private const string InvalidConnectionStringMessage = "Not a valid Redis connection string (expected host:6379[,password=…][,ssl=true], or redis://[user:password@]host[:port][/db] — rediss:// for TLS).";

    private static bool IsStreamMode(BrokerOptions? options) => options?[ModeOption] == StreamMode;

    private static string EscapeGlob(string text) => Regex.Replace(text, @"[\\*?\[\]]", @"\$0");

    private static async Task<MessageListenResult> ListenToChannelAsync(ISubscriber subscriber, ChannelPattern channel, TimeSpan timeout, CancellationToken setupToken, CancellationToken cancellationToken, Action onListening)
    {
        var queue = await subscriber.SubscribeAsync(SubscriptionOf(channel)).WaitAsync(setupToken);
        // The PING goes over the subscription connection after SUBSCRIBE, and the server answers
        // in order, so the subscription is registered before the listen window starts.
        await subscriber.PingAsync().WaitAsync(setupToken);
        onListening();

        using var listenCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        listenCts.CancelAfter(timeout);
        while (true)
        {
            var message = await queue.ReadAsync(listenCts.Token);
            var channelName = message.Channel.ToString();
            if (Matches(channel, channelName))
            {
                return new MessageListenResult(true, $"Received on channel \"{channelName}\".", message.Message.ToString());
            }
        }
    }

    private static async Task<MessageListenResult> ListenToStreamAsync(IDatabase database, string key, TimeSpan timeout, CancellationToken setupToken, CancellationToken cancellationToken, Action onListening)
    {
        // What XREAD's "$" means: whatever is the last entry now. An empty or missing stream starts
        // from the beginning, so its first entry counts.
        var last = await database.StreamRangeAsync(key, "-", "+", count: 1, messageOrder: Order.Descending).WaitAsync(setupToken);
        var position = last.Length > 0 ? last[0].Id : (RedisValue)"0-0";
        onListening();

        using var listenCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        listenCts.CancelAfter(timeout);
        while (true)
        {
            var entries = await database.StreamReadAsync(key, position, count: 1).WaitAsync(listenCts.Token);
            if (entries.Length > 0)
            {
                return new MessageListenResult(true, $"Received entry {entries[0].Id} on stream \"{key}\".", PayloadOf(entries[0].Values));
            }

            await Task.Delay(StreamPollInterval, listenCts.Token);
        }
    }

    /// <summary>Connects with <paramref name="configuration"/>'s timeout; a connection abandoned by cancellation is disposed once it completes.</summary>
    private static async Task<ConnectionMultiplexer> ConnectAsync(ConfigurationOptions configuration, CancellationToken cancellationToken)
    {
        var connecting = ConnectionMultiplexer.ConnectAsync(configuration);
        try
        {
            return await connecting.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _ = connecting.ContinueWith(task => task.Result.Dispose(), CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
            throw;
        }
    }

    /// <summary>
    /// A message safe to show: StackExchange.Redis's own one for a failed connection advises on
    /// "abortConnect", which isn't something the user can set here.
    /// </summary>
    private static string MessageOf(Exception exception) => exception switch
    {
        RedisConnectionException { FailureType: ConnectionFailureType.AuthenticationFailure } => "The server refused the credentials.",
        RedisConnectionException connectionFailure => $"Couldn't connect to the server ({connectionFailure.FailureType}).",
        RedisTimeoutException => "The server didn't answer in time.",
        _ => exception.Message
    };
}
