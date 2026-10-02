namespace VroksNet.Web;

/// <summary>The body of a Test Scenario create/update. <see cref="ListenTimeoutSeconds"/>/<see cref="ListenExchange"/> only matter for <see cref="TestScenarioKind.Listen"/>; null means "use the server's default".</summary>
public sealed record TestScenarioForm(
    string Name,
    Guid SpecificationId,
    Guid MockEndpointId,
    Guid ConnectionId,
    string? PayloadOverride,
    TestScenarioKind Kind,
    int? ListenTimeoutSeconds,
    string? ListenExchange);
