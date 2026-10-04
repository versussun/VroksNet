namespace VroksNet.Domain.Connections;

/// <summary>
/// What a <see cref="ConnectionServiceType"/> is and can do (ADR 0003) — the one place that says
/// so. Domain rules read these instead of comparing enum values, and the Admin UI gets them from
/// <c>GET /api/system/connection-types</c> instead of listing the types itself. A new type needs an
/// entry here and a broker adapter in Infrastructure; a unit test keeps the two consistent.
/// </summary>
/// <param name="IsHttp">Its operations are HTTP requests ("METHOD /path"); otherwise it's a broker and takes AsyncAPI operations.</param>
/// <param name="CanListen">A Listen scenario can wait for messages through it without taking them from real consumers.</param>
/// <param name="ListenNote">Why it can't be listened on; null when it can.</param>
/// <param name="ValueLabel">What <see cref="Connection.Value"/> holds for it — "URL" or "Connection string".</param>
/// <param name="ValueHint">An example value, for the UI's placeholder.</param>
/// <param name="Protocols">The AsyncAPI <c>servers.*.protocol</c> values it speaks (lower-case), so a spec can say which connections fit it.</param>
public sealed record ServiceTypeTraits(
    ConnectionServiceType Type,
    string DisplayName,
    bool IsHttp,
    bool CanListen,
    string? ListenNote,
    string ValueLabel,
    string ValueHint,
    IReadOnlyList<string> Protocols)
{
    private const string ConnectionString = "Connection string";
    private const string AwsValueHint = "Region=eu-west-1;AccessKeyId=…;SecretAccessKey=… — or Region=… alone for the default AWS credentials; ServiceUrl=http://… for an emulator";

    /// <summary>Every type, in the order the UI offers them.</summary>
    public static IReadOnlyList<ServiceTypeTraits> All { get; } =
    [
        new(ConnectionServiceType.Http, "HTTP", IsHttp: true, CanListen: false,
            "An HTTP connection has no channel to listen on — Listen needs a broker connection.",
            // None: an AsyncAPI server's "http" can't take a broker operation through an HTTP connection.
            "URL", "https://api.example.com", []),
        new(ConnectionServiceType.RabbitMq, "RabbitMQ", IsHttp: false, CanListen: true, null,
            ConnectionString, "amqp://user:password@host:5672/vhost", ["amqp", "amqps"]),
        new(ConnectionServiceType.Nats, "NATS", IsHttp: false, CanListen: true, null,
            ConnectionString, "nats://user:password@host:4222", ["nats"]),
        new(ConnectionServiceType.Kafka, "Kafka", IsHttp: false, CanListen: true, null,
            ConnectionString, "host:9092,host2:9092 — or bootstrap.servers=host:9092;security.protocol=SASL_SSL;… — or an Event Hubs Endpoint=sb://…", ["kafka", "kafka-secure"]),
        new(ConnectionServiceType.Mqtt, "MQTT", IsHttp: false, CanListen: true, null,
            ConnectionString, "mqtt://user:password@host:1883 — or mqtts://… for TLS", ["mqtt", "secure-mqtt"]),
        new(ConnectionServiceType.Redis, "Redis", IsHttp: false, CanListen: true, null,
            ConnectionString, "host:6379,password=… — or redis://user:password@host:6379/0 (rediss://… for TLS)", ["redis"]),
        // Listen works on topics only (through a subscription); a queue is Send only, which can't be
        // told from the type — the adapter says so when a Listen meets a queue.
        new(ConnectionServiceType.ServiceBus, "Azure Service Bus", IsHttp: false, CanListen: true, null,
            ConnectionString, "Endpoint=sb://namespace.servicebus.windows.net/;SharedAccessKeyName=…;SharedAccessKey=…", ["servicebus", "sb"]),
        new(ConnectionServiceType.Sqs, "AWS SQS", IsHttp: false, CanListen: false,
            "An SQS queue's consumers compete for its messages, so listening on one would take them away — listen on an SNS topic instead.",
            ConnectionString, AwsValueHint, ["sqs"]),
        new(ConnectionServiceType.Sns, "AWS SNS", IsHttp: false, CanListen: true, null,
            ConnectionString, AwsValueHint, ["sns"]),
    ];

    private static readonly Dictionary<ConnectionServiceType, ServiceTypeTraits> ByType = All.ToDictionary(traits => traits.Type);

    /// <summary>The traits of <paramref name="type"/>; null for a value that isn't a known type (e.g. a stray number from a request).</summary>
    public static ServiceTypeTraits? Find(ConnectionServiceType type) => ByType.GetValueOrDefault(type);

    /// <summary>The types that speak any of <paramref name="protocols"/> (case-insensitive), in <see cref="All"/>'s order; empty when none does.</summary>
    public static IReadOnlyList<ConnectionServiceType> TypesFor(IEnumerable<string> protocols)
    {
        var wanted = protocols.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return All.Where(traits => traits.Protocols.Any(wanted.Contains)).Select(traits => traits.Type).ToList();
    }
}
