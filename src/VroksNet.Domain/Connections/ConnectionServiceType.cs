namespace VroksNet.Domain.Connections;

/// <summary>What kind of system a <see cref="Connection"/> points at — determines how its <see cref="Connection.Value"/> is interpreted (a URL vs. a connection string).</summary>
public enum ConnectionServiceType
{
    Http,
    RabbitMq,
    Nats,
    Kafka,
    Mqtt,
    Redis,
    ServiceBus
}
