using Mediator;

namespace VroksNet.Application.TestScenarios.RunTestScenario;

/// <summary>Result is null if no scenario with <see cref="Id"/> exists.</summary>
public sealed record RunTestScenario(Guid Id) : IRequest<RunTestScenarioResult?>;
