using Mediator;
using VroksNet.Application.Abstractions;
using VroksNet.Application.TestScenarios.ListTestScenarios;

namespace VroksNet.Application.TestScenarios.GetTestScenario;

/// <summary>Single-scenario counterpart to <c>ListTestScenariosHandler</c> — same denormalized shape (<see cref="TestScenarioSummary"/>, via the same <see cref="TestScenarioSummaryFactory"/>), including its last-run status, for polling one scenario's status without listing all of them.</summary>
public sealed class GetTestScenarioHandler(
    ITestScenarioRepository scenarios,
    IApiSpecificationRepository specifications,
    IConnectionRepository connections) : IRequestHandler<GetTestScenario, TestScenarioSummary?>
{
    public async ValueTask<TestScenarioSummary?> Handle(GetTestScenario request, CancellationToken cancellationToken)
    {
        var scenario = await scenarios.FindByIdAsync(request.Id, cancellationToken);
        if (scenario is null)
        {
            return null;
        }

        var specification = await specifications.FindByIdAsync(scenario.SpecificationId, cancellationToken);
        var connection = await connections.FindByIdAsync(scenario.ConnectionId, cancellationToken);

        return TestScenarioSummaryFactory.Build(scenario, specification, connection);
    }
}
