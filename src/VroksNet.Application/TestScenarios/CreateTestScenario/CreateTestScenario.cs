using Mediator;
using VroksNet.Domain.TestScenarios;

namespace VroksNet.Application.TestScenarios.CreateTestScenario;

/// <summary>
/// <see cref="ListenTimeoutSeconds"/> only applies to <see cref="TestScenarioKind.Listen"/>; it's dropped otherwise.
/// <see cref="BrokerOptions"/> must be options the connection's type declares (ADR 0003); <see cref="Exchange"/> is the deprecated
/// spelling of <c>BrokerOptions["exchange"]</c>, dropped for a type without that option, as it always was.
/// <see cref="Schedule"/> is a 5-field cron expression read in <see cref="ScheduleTimeZone"/> (IANA; UTC if null) — the background worker runs the scenario on it (ADR 0002).
/// </summary>
public sealed record CreateTestScenario(
    string Name,
    Guid SpecificationId,
    Guid MockEndpointId,
    Guid ConnectionId,
    string? PayloadOverride,
    TestScenarioKind Kind = TestScenarioKind.Send,
    int? ListenTimeoutSeconds = null,
    string? Exchange = null,
    string? Schedule = null,
    string? ScheduleTimeZone = null,
    IReadOnlyDictionary<string, string?>? BrokerOptions = null) : IRequest<Guid>;
