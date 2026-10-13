namespace VroksNet.Domain.Connections;

/// <summary>What kind of system a <see cref="Connection"/> points at — determines how its <see cref="Connection.Value"/> is interpreted (a URL vs. a connection string). Stored as its number, so new types go at the end.</summary>
public enum ConnectionServiceType
{
    Http,
    RabbitMq,
    Nats,
    Kafka,
    Mqtt,
    Redis,
    ServiceBus,
    Sqs,
    Sns,

    /// <summary>A gRPC server (ADR 0004): <see cref="Connection.Value"/> is its URL, <c>http://</c> for h2c or <c>https://</c>.</summary>
    Grpc
}
