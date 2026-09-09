using Mediator;

namespace VroksNet.Application.TestScenarios.ListTestScenarios;

public sealed record ListTestScenarios : IRequest<IReadOnlyList<TestScenarioSummary>>;
