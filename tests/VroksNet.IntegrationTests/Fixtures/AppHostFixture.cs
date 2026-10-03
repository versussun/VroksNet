namespace VroksNet.IntegrationTests.Fixtures;

/// <summary>
/// The core graph: RabbitMQ, NATS and Kafka, which the required "Integration tests" CI job covers.
/// Shared by every test in the "AppHost" collection (<see cref="AppHostCollection"/>).
/// </summary>
public sealed class AppHostFixture() : AppHostFixtureBase(CoreBrokers)
{
    /// <summary>AppHost resource names (AppHost.cs "Brokers").</summary>
    public static readonly string[] CoreBrokers = ["rabbitmq", "nats", "kafka"];
}
