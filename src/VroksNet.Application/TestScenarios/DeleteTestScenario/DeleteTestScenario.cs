using Mediator;

namespace VroksNet.Application.TestScenarios.DeleteTestScenario;

/// <summary>Result is false if no scenario with that id exists.</summary>
public sealed record DeleteTestScenario(Guid Id) : IRequest<bool>;
