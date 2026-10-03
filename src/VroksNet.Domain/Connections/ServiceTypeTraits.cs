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
public sealed record ServiceTypeTraits(
    ConnectionServiceType Type,
    string DisplayName,
    bool IsHttp,
    bool CanListen,
    string? ListenNote,
    string ValueLabel,
    string ValueHint)
{
    private const string ConnectionString = "Connection string";

    /// <summary>Every type, in the order the UI offers them.</summary>
    public static IReadOnlyList<ServiceTypeTraits> All { get; } =
    [
        new(ConnectionServiceType.Http, "HTTP", IsHttp: true, CanListen: false,
            "An HTTP connection has no channel to listen on — Listen needs a broker connection.",
            "URL", "https://api.example.com"),
        new(ConnectionServiceType.RabbitMq, "RabbitMQ", IsHttp: false, CanListen: true, null,
            ConnectionString, "amqp://user:password@host:5672/vhost"),
        new(ConnectionServiceType.Nats, "NATS", IsHttp: false, CanListen: true, null,
            ConnectionString, "nats://user:password@host:4222"),
        new(ConnectionServiceType.Kafka, "Kafka", IsHttp: false, CanListen: true, null,
            ConnectionString, "host:9092,host2:9092 — or bootstrap.servers=host:9092;security.protocol=SASL_SSL;…"),
    ];

    private static readonly Dictionary<ConnectionServiceType, ServiceTypeTraits> ByType = All.ToDictionary(traits => traits.Type);

    /// <summary>The traits of <paramref name="type"/>; null for a value that isn't a known type (e.g. a stray number from a request).</summary>
    public static ServiceTypeTraits? Find(ConnectionServiceType type) => ByType.GetValueOrDefault(type);
}
