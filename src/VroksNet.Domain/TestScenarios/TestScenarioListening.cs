namespace VroksNet.Domain.TestScenarios;

/// <summary>Rules for <see cref="TestScenarioKind.Listen"/> scenarios.</summary>
public static class TestScenarioListening
{
    public const int DefaultTimeoutSeconds = 30;

    /// <summary>
    /// A run holds its HTTP request open while it waits. With up to 10s more to reach the broker,
    /// 80s keeps the whole run under the Admin UI's 100s HttpClient timeout.
    /// </summary>
    public const int MaxTimeoutSeconds = 80;

    /// <summary>RabbitMQ's built-in topic exchange, used when a scenario names none.</summary>
    public const string DefaultRabbitMqExchange = "amq.topic";

    /// <summary>
    /// Whether the operation is a broker channel that can be subscribed to. HTTP operations have no
    /// channel; an AsyncAPI address whose parameters aren't whole "."-separated segments (e.g.
    /// "user/{id}/signedup") can't be turned into a subscription — see <see cref="SubscriptionPatternOf"/>.
    /// </summary>
    public static bool CanListen(string operationKey)
        => OperationCompatibility.ChannelAddressOf(operationKey) is { } address && SubscriptionPatternOf(address) is not null;

    /// <summary>
    /// The RabbitMQ topic binding key / NATS subject to subscribe with for a channel address: each
    /// "."-separated segment that is a whole AsyncAPI parameter ("{region}") becomes the one-segment
    /// wildcard "*", which means the same thing in both brokers ("orders.{region}.created" →
    /// "orders.*.created"). Null if a parameter is only part of a segment, since neither broker can
    /// match that. Addresses without parameters are used as-is.
    /// </summary>
    public static string? SubscriptionPatternOf(string channelAddress)
    {
        var segments = channelAddress.Split('.');
        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            if (segment.Length > 2 && segment[0] == '{' && segment[^1] == '}' && segment.IndexOfAny(['{', '}'], 1, segment.Length - 2) < 0)
            {
                segments[i] = "*";
            }
            else if (segment.Contains('{') || segment.Contains('}'))
            {
                return null;
            }
        }

        return string.Join('.', segments);
    }

    /// <summary>
    /// The mode a new scenario should start in. An AsyncAPI "send" operation is one the described
    /// service publishes, so testing it means listening for its messages; anything else (a
    /// "receive" operation the service consumes, or an HTTP operation) means sending to it.
    /// </summary>
    public static TestScenarioKind DefaultKindFor(string operationKey)
        => CanListen(operationKey) && operationKey.EndsWith(":send", StringComparison.Ordinal) ? TestScenarioKind.Listen : TestScenarioKind.Send;
}
