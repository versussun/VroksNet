namespace VroksNet.Web;

/// <summary>The body of a Test Scenario create/update. <see cref="ListenTimeoutSeconds"/> only matters for <see cref="TestScenarioKind.Listen"/>, <see cref="BrokerOptions"/> must be options the connection's type declares; null means "use the server's defaults". A null <see cref="Schedule"/> means not scheduled.</summary>
public sealed record TestScenarioForm(
    string Name,
    Guid SpecificationId,
    Guid MockEndpointId,
    Guid ConnectionId,
    string? PayloadOverride,
    TestScenarioKind Kind,
    int? ListenTimeoutSeconds,
    Dictionary<string, string>? BrokerOptions,
    string? Schedule,
    string? ScheduleTimeZone);
