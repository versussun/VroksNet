namespace VroksNet.Web;

/// <summary>The body of a Test Scenario create/update. <see cref="ListenTimeoutSeconds"/> only matters for <see cref="TestScenarioKind.Listen"/>, <see cref="Exchange"/> only for a RabbitMQ connection; null means "use the server's default".</summary>
public sealed record TestScenarioForm(
    string Name,
    Guid SpecificationId,
    Guid MockEndpointId,
    Guid ConnectionId,
    string? PayloadOverride,
    TestScenarioKind Kind,
    int? ListenTimeoutSeconds,
    string? Exchange);
