using Mediator;
using VroksNet.Domain.TestScenarios;

namespace VroksNet.Application.TestScenarios.UpdateTestScenario;

/// <summary>Result is false if no scenario with <see cref="Id"/> exists. Listen timeout, exchange and schedule behave as in <see cref="CreateTestScenario.CreateTestScenario"/>.</summary>
public sealed record UpdateTestScenario(
    Guid Id,
    string Name,
    Guid SpecificationId,
    Guid MockEndpointId,
    Guid ConnectionId,
    string? PayloadOverride,
    TestScenarioKind Kind = TestScenarioKind.Send,
    int? ListenTimeoutSeconds = null,
    string? Exchange = null,
    string? Schedule = null,
    string? ScheduleTimeZone = null) : IRequest<bool>;
