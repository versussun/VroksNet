using Mediator;
using VroksNet.Application.Abstractions;

namespace VroksNet.Application.TestSuites.ListTestSuites;

public sealed class ListTestSuitesHandler(
    ITestSuiteRepository suites,
    ISuiteRunRepository suiteRuns,
    ITestScenarioRepository scenarios,
    ITestRunRepository runs) : IRequestHandler<ListTestSuites, IReadOnlyList<TestSuiteSummary>>
{
    public async ValueTask<IReadOnlyList<TestSuiteSummary>> Handle(ListTestSuites request, CancellationToken cancellationToken)
    {
        var all = await suites.ListAsync(cancellationToken);
        var scenariosById = await SuiteRunDetailsFactory.ScenariosByIdAsync(scenarios, cancellationToken);
        var summaries = new List<TestSuiteSummary>(all.Count);
        foreach (var suite in all)
        {
            summaries.Add(await TestSuiteSummaryFactory.BuildAsync(suite, suiteRuns, runs, scenariosById, cancellationToken));
        }

        return summaries;
    }
}
