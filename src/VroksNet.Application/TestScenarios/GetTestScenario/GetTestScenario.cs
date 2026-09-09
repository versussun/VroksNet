using Mediator;
using VroksNet.Application.TestScenarios.ListTestScenarios;

namespace VroksNet.Application.TestScenarios.GetTestScenario;

/// <summary>Result is null if no scenario with <see cref="Id"/> exists.</summary>
public sealed record GetTestScenario(Guid Id) : IRequest<TestScenarioSummary?>;
