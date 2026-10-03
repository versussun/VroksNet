using Mediator;
using VroksNet.Application.Abstractions;

namespace VroksNet.Application.TestScenarios.ListTestScenarios;

public sealed class ListTestScenariosHandler(
    ITestScenarioRepository scenarios,
    IApiSpecificationRepository specifications,
    IConnectionRepository connections,
    ICronSchedule cron,
    TimeProvider timeProvider) : IRequestHandler<ListTestScenarios, IReadOnlyList<TestScenarioSummary>>
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
        var now = timeProvider.GetUtcNow();

        return all
            .Select(scenario => TestScenarioSummaryFactory.Build(
                scenario,
                specsById.GetValueOrDefault(scenario.SpecificationId),
                connectionsById.GetValueOrDefault(scenario.ConnectionId),
                cron,
                now))
            .OrderBy(summary => summary.Name)
            .ToList();
    }
}
