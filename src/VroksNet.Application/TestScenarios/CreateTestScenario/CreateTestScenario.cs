using Mediator;

namespace VroksNet.Application.TestScenarios.CreateTestScenario;

public sealed record CreateTestScenario(string Name, Guid SpecificationId, Guid MockEndpointId, Guid ConnectionId, string? PayloadOverride) : IRequest<Guid>;
