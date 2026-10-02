namespace VroksNet.Domain.TestScenarios;

/// <summary>What a <see cref="TestScenario"/> does when it's run.</summary>
public enum TestScenarioKind
{
    /// <summary>Send the operation's message: an HTTP request, or a broker publish.</summary>
    Send = 0,

    /// <summary>Wait for the next message on the operation's broker channel and validate it against the spec (AsyncAPI operations only).</summary>
    Listen = 1
}
