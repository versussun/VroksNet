using System.Text.RegularExpressions;
using Confluent.Kafka;

namespace VroksNet.Infrastructure.Connections;

/// <summary>
/// Builds the short-lived Kafka clients <see cref="ConnectionTester"/>, <see cref="MessageSender"/>
/// and <see cref="MessageListener"/> use. A Kafka connection's value is either a plain
/// bootstrap-servers list ("host:9092[,host2:9092]", what Aspire's Kafka resource hands out) or
/// librdkafka settings as "key=value;key=value" — the latter for clusters that need SASL/TLS.
/// The client's own socket/delivery timeouts are set to the caller's budget so nothing outlives
/// it, and librdkafka's log/error output is swallowed: it would otherwise go straight to stderr,
/// and every failure the caller cares about surfaces as an exception anyway.
/// </summary>
internal static class KafkaClients
{
    public const string InvalidConnectionStringMessage =
        "Not a valid Kafka connection string (expected host:port[,host:port] or key=value;… settings including bootstrap.servers).";

    /// <summary>The client settings a connection value describes; null if it names no bootstrap servers or has a malformed "key=value" pair.</summary>
    public static Dictionary<string, string>? ConfigFrom(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!value.Contains('='))
        {
            return new Dictionary<string, string> { ["bootstrap.servers"] = value.Trim() };
        }

        var config = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separatorIndex = pair.IndexOf('=');
            if (separatorIndex <= 0)
            {
                return null;
            }

            config[pair[..separatorIndex].Trim()] = pair[(separatorIndex + 1)..].Trim();
        }

        return config.TryGetValue("bootstrap.servers", out var servers) && servers.Length > 0 ? config : null;
    }

    public static IAdminClient CreateAdminClient(Dictionary<string, string> config, TimeSpan timeout)
        => new AdminClientBuilder(WithTimeouts(config, timeout))
            .SetLogHandler((_, _) => { })
            .SetErrorHandler((_, _) => { })
            .Build();

    public static IProducer<Null, string> CreateProducer(Dictionary<string, string> config, TimeSpan timeout)
    {
        var settings = WithTimeouts(config, timeout);
        // Bounds the whole delivery, so ProduceAsync to an unreachable broker fails instead of retrying forever.
        settings["message.timeout.ms"] = Milliseconds(timeout);
        return new ProducerBuilder<Null, string>(settings)
            .SetLogHandler((_, _) => { })
            .SetErrorHandler((_, _) => { })
            .Build();
    }

    /// <summary>
    /// A consumer meant for <see cref="IConsumer{TKey,TValue}.Assign(IEnumerable{TopicPartitionOffset})"/>
    /// only: it never joins its (throwaway, unique) group or commits, so the broker keeps no state
    /// for it and the topic's real consumer groups are untouched.
    /// </summary>
    public static IConsumer<Ignore, string> CreateConsumer(Dictionary<string, string> config, TimeSpan timeout)
    {
        var settings = WithTimeouts(config, timeout);
        settings["group.id"] = $"vroksnet-listen-{Guid.NewGuid():N}";
        settings["enable.auto.commit"] = "false";
        settings["enable.auto.offset.store"] = "false";
        return new ConsumerBuilder<Ignore, string>(settings)
            .SetLogHandler((_, _) => { })
            .SetErrorHandler((_, _) => { })
            .Build();
    }

    /// <summary>
    /// The topic regex for a subscription pattern from
    /// <see cref="Domain.TestScenarios.TestScenarioListening.SubscriptionPatternOf"/>: each "*"
    /// segment matches one "."-separated segment, everything else literally ("orders.*.created" →
    /// ^orders\.[^.]+\.created$).
    /// </summary>
    public static Regex TopicRegexOf(string subscriptionPattern)
        => new("^" + string.Join(@"\.", subscriptionPattern.Split('.').Select(segment => segment == "*" ? "[^.]+" : Regex.Escape(segment))) + "$");

    private static Dictionary<string, string> WithTimeouts(Dictionary<string, string> config, TimeSpan timeout)
    {
        var settings = new Dictionary<string, string>(config, StringComparer.OrdinalIgnoreCase);
        settings["socket.timeout.ms"] = Milliseconds(timeout);
        settings["socket.connection.setup.timeout.ms"] = Milliseconds(timeout);
        return settings;
    }

    private static string Milliseconds(TimeSpan timeout) => ((int)timeout.TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
}
