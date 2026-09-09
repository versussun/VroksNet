using Mediator;
using VroksNet.Application.Abstractions;

namespace VroksNet.Application.TestScenarios.ListTestScenarios;

public sealed class ListTestScenariosHandler(
    ITestScenarioRepository scenarios,
    IApiSpecificationRepository specifications,
    IConnectionRepository connections) : IRequestHandler<ListTestScenarios, IReadOnlyList<TestScenarioSummary>>
{
    public async ValueTask<IReadOnlyList<TestScenarioSummary>> Handle(ListTestScenarios request, CancellationToken cancellationToken)
    {
        var all = await scenarios.ListAsync(cancellationToken);
        if (all.Count == 0)
        {
            return [];
        }

        var specsById = (await specifications.ListAsync(cancellationToken)).ToDictionary(s => s.Id);
        var connectionsById = (await connections.ListAsync(cancellationToken)).ToDictionary(c => c.Id);

        return all
            .Select(scenario =>
            {
                specsById.TryGetValue(scenario.SpecificationId, out var specification);
                var endpoint = specification?.Endpoints.FirstOrDefault(e => e.Id == scenario.MockEndpointId);
                connectionsById.TryGetValue(scenario.ConnectionId, out var connection);

                return new TestScenarioSummary(
                    scenario.Id,
                    scenario.Name,
                    scenario.SpecificationId,
                    specification?.Title ?? "(deleted specification)",
                    scenario.MockEndpointId,
                    endpoint?.OperationKey ?? "(deleted operation)",
                    scenario.ConnectionId,
                    connection?.Name ?? "(deleted connection)",
                    connection?.ServiceType ?? default,
                    scenario.UpdatedAt);
            })
            .OrderBy(summary => summary.Name)
            .ToList();
    }
}
