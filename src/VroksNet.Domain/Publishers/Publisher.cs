namespace VroksNet.Domain.Publishers;

/// <summary>
/// An async mock: publishes one AsyncAPI operation's message (<see cref="MockEndpointId"/> from
/// <see cref="SpecificationId"/>) through a RabbitMQ/NATS/Kafka <see cref="Connections.Connection"/>
/// (<see cref="ConnectionId"/>) every <see cref="IntervalSeconds"/> while <see cref="IsEnabled"/>,
/// or on demand. Unlike a <see cref="TestScenarios.TestScenario"/> (a one-off check), it's
/// background work. No foreign-key constraints to what it references — same loose coupling as
/// <see cref="TestScenarios.TestScenario"/>: a deleted reference makes its publishes fail, not cascade.
/// </summary>
public sealed class Publisher
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public Guid SpecificationId { get; set; }

    public Guid MockEndpointId { get; set; }

    public Guid ConnectionId { get; set; }

    /// <summary>Published instead of the operation's own example, if set. Either way it's a template: <c>{{uuid}}</c>/<c>{{now}}</c> are filled in per message.</summary>
    public string? PayloadOverride { get; set; }

    /// <summary>RabbitMQ only (null for NATS): the exchange to publish to — the default exchange ("", straight into the queue named after the channel) if null.</summary>
    public string? Exchange { get; set; }

    /// <summary>Seconds between publishes, from <see cref="PublisherSchedule.MinIntervalSeconds"/> to <see cref="PublisherSchedule.MaxIntervalSeconds"/>.</summary>
    public int IntervalSeconds { get; set; }

    /// <summary>Whether it publishes on its schedule. A disabled publisher can still be published on demand.</summary>
    public bool IsEnabled { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>When the last publish (scheduled or on demand) started — the schedule counts the next one from here. Null until the first.</summary>
    public DateTimeOffset? LastPublishedAt { get; set; }

    public bool? LastPublishSuccess { get; set; }

    /// <summary>Safe to show verbatim in the UI/API (same contract as the message sender's result message).</summary>
    public string? LastPublishMessage { get; set; }
}
