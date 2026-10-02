using Mediator;
using VroksNet.Domain.TestScenarios;

namespace VroksNet.Application.TestScenarios.CreateTestScenario;

/// <summary><see cref="ListenTimeoutSeconds"/> only applies to <see cref="TestScenarioKind.Listen"/>, and <see cref="Exchange"/> only to a RabbitMQ connection (either kind); they're dropped otherwise.</summary>
public sealed record CreateTestScenario(
    string Name,
    Guid SpecificationId,
    Guid MockEndpointId,
    Guid ConnectionId,
    string? PayloadOverride,
    TestScenarioKind Kind = TestScenarioKind.Send,
    int? ListenTimeoutSeconds = null,
    string? Exchange = null) : IRequest<Guid>;
