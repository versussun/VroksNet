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
    /// The connection's broker-specific settings (ADR 0003), e.g. RabbitMQ's <c>exchange</c> — what
    /// they mean, and their defaults, are up to the connection type's broker adapter. Null when
    /// none are set.
    /// </summary>
    public Connections.BrokerOptions? BrokerOptions { get; set; }

    /// <summary>
    /// A standard 5-field cron expression (ADR 0002) the background worker runs this scenario on,
    /// e.g. <c>0 9 * * 1-5</c>; null when it isn't scheduled. Validated by the Application layer's
    /// <c>ICronSchedule</c>, not here: Domain stays free of the parser.
    /// </summary>
    public string? Schedule { get; set; }

    /// <summary>The IANA time zone <see cref="Schedule"/> is read in (<c>Europe/Kyiv</c>); null means UTC. Only set together with <see cref="Schedule"/>.</summary>
    public string? ScheduleTimeZone { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// When provisioning (ADR 0001) last brought this scenario in line with the manifest or the specs
    /// folder; null for one created in the UI or the API. A provisioned object is overwritten on
    /// every start — UI edits to it don't stick.
    /// </summary>
    public DateTimeOffset? ProvisionedAt { get; set; }

    /// <summary>When this scenario was last <c>Run</c>, and with what result — null until the first run. Distinct from <see cref="UpdatedAt"/>, which tracks edits to the scenario's own definition, not runs of it.</summary>
    public DateTimeOffset? LastRunAt { get; set; }

    public bool? LastRunSuccess { get; set; }

    /// <summary>Safe to show verbatim in the UI/API — never a raw connection string or full exception stack (same contract as IMessageSender's MessageSendResult.Message).</summary>
    public string? LastRunMessage { get; set; }
}
