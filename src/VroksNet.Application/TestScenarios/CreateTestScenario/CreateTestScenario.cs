using Mediator;
using VroksNet.Domain.TestScenarios;

namespace VroksNet.Application.TestScenarios.CreateTestScenario;

/// <summary><see cref="ListenTimeoutSeconds"/>/<see cref="ListenExchange"/> only apply to <see cref="TestScenarioKind.Listen"/> and are dropped otherwise.</summary>
public sealed record CreateTestScenario(
    string Name,
    Guid SpecificationId,
    Guid MockEndpointId,
    Guid ConnectionId,
    string? PayloadOverride,
    TestScenarioKind Kind = TestScenarioKind.Send,
    int? ListenTimeoutSeconds = null,
    string? ListenExchange = null) : IRequest<Guid>;
