namespace VroksNet.Domain.TestScenarios;

/// <summary>Rules for <see cref="TestScenarioKind.Listen"/> scenarios.</summary>
public static class TestScenarioListening
{
    public const int DefaultTimeoutSeconds = 30;

    /// <summary>
    /// The longest a Listen scenario may wait, in seconds — 30 minutes. Only a background run
    /// (ADR 0002) can wait that long; a synchronous one is capped at <see cref="MaxSynchronousTimeoutSeconds"/>.
    /// </summary>
    public const int MaxTimeoutSeconds = 1800;

    /// <summary>
    /// The longest a synchronous run may wait. It holds its HTTP request open while it waits, and
    /// with up to 10s more to reach the broker, 80s keeps the whole run under the Admin UI's 100s
    /// HttpClient timeout. A scenario that waits longer can only run in the background.
    /// </summary>
    public const int MaxSynchronousTimeoutSeconds = 80;

    /// <summary>Whether a scenario's Listen timeout is too long for a synchronous run.</summary>
    public static bool RequiresBackgroundRun(TestScenarioKind kind, int? timeoutSeconds)
        => kind == TestScenarioKind.Listen && (timeoutSeconds ?? DefaultTimeoutSeconds) > MaxSynchronousTimeoutSeconds;

    /// <summary>RabbitMQ's built-in topic exchange, used when a scenario names none.</summary>
    public const string DefaultRabbitMqExchange = "amq.topic";

    /// <summary>
    /// Whether the operation is a broker channel that can be subscribed to. HTTP operations have no
    /// channel; an AsyncAPI address with a parameter that is only part of a segment
    /// ("orders.eu-{region}.created") can't be turned into a subscription — see
    /// <see cref="ChannelPattern.Parse"/>. Whether a particular broker can match the pattern is up
    /// to its adapter.
    /// </summary>
    public static bool CanListen(string operationKey)
        => OperationCompatibility.ChannelAddressOf(operationKey) is { } address && ChannelPattern.Parse(address) is not null;

    /// <summary>
    /// The mode a new scenario should start in. An AsyncAPI "send" operation is one the described
    /// service publishes, so testing it means listening for its messages; anything else (a
    /// "receive" operation the service consumes, or an HTTP operation) means sending to it.
    /// </summary>
    public static TestScenarioKind DefaultKindFor(string operationKey)
        => CanListen(operationKey) && operationKey.EndsWith(":send", StringComparison.Ordinal) ? TestScenarioKind.Listen : TestScenarioKind.Send;
}
