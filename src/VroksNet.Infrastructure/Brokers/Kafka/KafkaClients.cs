using Confluent.Kafka;

namespace VroksNet.Infrastructure.Brokers.Kafka;

/// <summary>
/// Builds the short-lived Kafka clients <see cref="KafkaBrokerAdapter"/> uses. A Kafka connection's value is one of:
/// <list type="bullet">
/// <item>a plain bootstrap-servers list ("host:9092[,host2:9092]", what Aspire's Kafka resource hands out);</item>
/// <item>librdkafka settings as "key=value;key=value" — for clusters that need SASL/TLS. A value may
/// be double-quoted to contain ";" and "=" (<c>sasl.password="Endpoint=sb://…;…"</c>), with "" for a quote;</item>
/// <item>an Azure Event Hubs connection string ("Endpoint=sb://…;SharedAccessKeyName=…;SharedAccessKey=…"),
/// which is turned into the settings of its Kafka endpoint (N1 of docs/broker-adapters-plan.md).</item>
/// </list>
/// The client's own socket/delivery timeouts are set to the caller's budget so nothing outlives
/// it, and librdkafka's log/error output is swallowed: it would otherwise go straight to stderr,
/// and every failure the caller cares about surfaces as an exception anyway.
/// </summary>
public static class KafkaClients
{
    public const string InvalidConnectionStringMessage =
        "Not a valid Kafka connection string (expected host:port[,host:port], key=value;… settings including bootstrap.servers, or an Event Hubs connection string Endpoint=sb://…).";

    /// <summary>
    /// The client settings a connection value describes; null if it names no bootstrap servers, has a
    /// malformed "key=value" pair or an unterminated quote, or is an Event Hubs connection string without an endpoint host.
    /// </summary>
    public static Dictionary<string, string>? ConfigFrom(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith("Endpoint=", StringComparison.OrdinalIgnoreCase))
        {
            return EventHubsConfigFrom(trimmed);
        }

        if (!trimmed.Contains('='))
        {
            return new Dictionary<string, string> { ["bootstrap.servers"] = trimmed };
        }

        if (PairsOf(trimmed) is not { } pairs)
        {
            return null;
        }

        var config = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, pairValue) in pairs)
        {
            config[key] = pairValue;
        }

        return config.TryGetValue("bootstrap.servers", out var servers) && servers.Length > 0 ? config : null;
    }

    /// <summary>
    /// Event Hubs' Kafka endpoint: the namespace host on 9093 over SASL_SSL, or — for the emulator
    /// (<c>UseDevelopmentEmulator=true</c>) — on 9092 over SASL_PLAINTEXT; PLAIN, with the user name
    /// <c>$ConnectionString</c> and the whole connection string as the password. A port in the
    /// endpoint is the AMQP one, so it's ignored.
    /// </summary>
    private static Dictionary<string, string>? EventHubsConfigFrom(string connectionString)
    {
        var parts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separatorIndex = part.IndexOf('=');
            if (separatorIndex > 0)
            {
                parts[part[..separatorIndex].Trim()] = part[(separatorIndex + 1)..].Trim();
            }
        }

        if (!parts.TryGetValue("Endpoint", out var endpoint) || !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Host.Length == 0)
        {
            return null;
        }

        var emulator = parts.TryGetValue("UseDevelopmentEmulator", out var flag) && bool.TryParse(flag, out var isEmulator) && isEmulator;
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["bootstrap.servers"] = $"{uri.Host}:{(emulator ? 9092 : 9093)}",
            ["security.protocol"] = emulator ? "SASL_PLAINTEXT" : "SASL_SSL",
            ["sasl.mechanism"] = "PLAIN",
            ["sasl.username"] = "$ConnectionString",
            ["sasl.password"] = connectionString
        };
    }

    /// <summary>
    /// "key=value" pairs separated by ";". A value in double quotes runs to its closing quote, so it
    /// may contain ";" and "="; "" inside it is one quote. Null on a pair without "=" or a key, an
    /// unterminated quote, or anything but ";" after a closing quote.
    /// </summary>
    private static List<KeyValuePair<string, string>>? PairsOf(string value)
    {
        var pairs = new List<KeyValuePair<string, string>>();
        var index = 0;
        while (true)
        {
            while (index < value.Length && (value[index] == ';' || char.IsWhiteSpace(value[index])))
            {
                index++;
            }

            if (index >= value.Length)
            {
                return pairs;
            }

            var separatorIndex = value.IndexOf('=', index);
            var nextPairIndex = value.IndexOf(';', index);
            if (separatorIndex < 0 || (nextPairIndex >= 0 && nextPairIndex < separatorIndex))
            {
                return null;
            }

            var key = value[index..separatorIndex].Trim();
            if (key.Length == 0)
            {
                return null;
            }

            index = separatorIndex + 1;
            while (index < value.Length && value[index] == ' ')
            {
                index++;
            }

            if (index < value.Length && value[index] == '"')
            {
                var quoted = new System.Text.StringBuilder();
                index++;
                while (true)
                {
                    if (index >= value.Length)
                    {
                        return null;
                    }

                    if (value[index] == '"')
                    {
                        if (index + 1 < value.Length && value[index + 1] == '"')
                        {
                            quoted.Append('"');
                            index += 2;
                            continue;
                        }

                        index++;
                        break;
                    }

                    quoted.Append(value[index++]);
                }

                while (index < value.Length && char.IsWhiteSpace(value[index]))
                {
                    index++;
                }

                if (index < value.Length && value[index] != ';')
                {
                    return null;
                }

                pairs.Add(new KeyValuePair<string, string>(key, quoted.ToString()));
            }
            else
            {
                var end = value.IndexOf(';', index);
                if (end < 0)
                {
                    end = value.Length;
                }

                pairs.Add(new KeyValuePair<string, string>(key, value[index..end].Trim()));
                index = end;
            }
        }
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
        // librdkafka treats a missing topic as "maybe still being created" for this long (30s by
        // default) before failing with UnknownTopic. Kept under the delivery timeout, so a missing
        // topic is reported as missing rather than as a broker that never acknowledged.
        settings["topic.metadata.propagation.max.ms"] = Milliseconds(timeout / 2);
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

    private static Dictionary<string, string> WithTimeouts(Dictionary<string, string> config, TimeSpan timeout)
    {
        var settings = new Dictionary<string, string>(config, StringComparer.OrdinalIgnoreCase);
        settings["socket.timeout.ms"] = Milliseconds(timeout);
        settings["socket.connection.setup.timeout.ms"] = Milliseconds(timeout);
        return settings;
    }

    private static string Milliseconds(TimeSpan timeout) => ((int)timeout.TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
}
