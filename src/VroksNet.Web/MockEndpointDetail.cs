namespace VroksNet.Web;

/// <summary>
/// The server decides <see cref="RequiresHttpConnection"/>, <see cref="CanListen"/> and
/// <see cref="DefaultTestScenarioKind"/> (the mode it suggests for a new Test Scenario — Listen for
/// an AsyncAPI "send" operation); the UI only reads them.
/// </summary>
public sealed record MockEndpointDetail(
    Guid Id,
    string OperationKey,
    bool IsEnabled,
    bool ServeAtRealPath,
    string? ExampleTemplate,
    bool RequiresHttpConnection,
    bool CanListen,
    TestScenarioKind DefaultTestScenarioKind);
