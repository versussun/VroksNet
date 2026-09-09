using Mediator;

namespace VroksNet.Application.TestScenarios.UpdateTestScenario;

/// <summary>Result is false if no scenario with <see cref="Id"/> exists.</summary>
public sealed record UpdateTestScenario(Guid Id, string Name, Guid SpecificationId, Guid MockEndpointId, Guid ConnectionId, string? PayloadOverride) : IRequest<bool>;
