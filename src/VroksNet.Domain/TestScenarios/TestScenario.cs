namespace VroksNet.Domain.TestScenarios;

/// <summary>
/// A saved "send this message somewhere" scenario: one operation (<see cref="MockEndpointId"/>)
/// from one imported specification (<see cref="SpecificationId"/>), sent through one
/// <see cref="Connections.Connection"/> (<see cref="ConnectionId"/>) — an HTTP service, or a
/// RabbitMQ/NATS/Kafka broker. No foreign-key constraints to the specification/endpoint/connection it
/// references (same loose-coupling as <see cref="CallRecords.CallRecord"/>) — deleting any of
/// those leaves a scenario that fails gracefully at run time rather than cascading.
/// </summary>
public sealed class TestScenario
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public Guid SpecificationId { get; set; }

    public Guid MockEndpointId { get; set; }

    public Guid ConnectionId { get; set; }

    /// <summary>Overrides the operation's own <see cref="MockEndpoints.MockEndpoint.ExampleTemplate"/> when sending, if set.</summary>
    public string? PayloadOverride { get; set; }

    public TestScenarioKind Kind { get; set; } = TestScenarioKind.Send;

    /// <summary>How long a <see cref="TestScenarioKind.Listen"/> run waits for a message — <see cref="TestScenarioListening.DefaultTimeoutSeconds"/> if null. Null for <see cref="TestScenarioKind.Send"/>.</summary>
    public int? ListenTimeoutSeconds { get; set; }

    /// <summary>
    /// RabbitMQ only (null for NATS/HTTP): the exchange a <see cref="TestScenarioKind.Send"/> run
    /// publishes to — the default exchange ("", i.e. straight into the queue named after the
    /// channel) if null — or a <see cref="TestScenarioKind.Listen"/> run binds its own temporary
    /// queue to — <see cref="TestScenarioListening.DefaultRabbitMqExchange"/> if null. The routing
    /// key is the channel address either way, so a Send and a Listen on the same operation and
    /// exchange meet.
    /// </summary>
    public string? Exchange { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>When this scenario was last <c>Run</c>, and with what result — null until the first run. Distinct from <see cref="UpdatedAt"/>, which tracks edits to the scenario's own definition, not runs of it.</summary>
    public DateTimeOffset? LastRunAt { get; set; }

    public bool? LastRunSuccess { get; set; }

    /// <summary>Safe to show verbatim in the UI/API — never a raw connection string or full exception stack (same contract as IMessageSender's MessageSendResult.Message).</summary>
    public string? LastRunMessage { get; set; }
}
