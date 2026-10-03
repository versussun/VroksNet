using Mediator;
using VroksNet.Application.Abstractions;

namespace VroksNet.Application.TestSuites.ListSuiteRuns;

public sealed class ListSuiteRunsHandler(
    ITestSuiteRepository suites,
    ISuiteRunRepository suiteRuns,
    ITestScenarioRepository scenarios,
    ITestRunRepository runs) : IRequestHandler<ListSuiteRuns, IReadOnlyList<SuiteRunDetails>?>
{
    private const int MaxLimit = 100;

    public async ValueTask<IReadOnlyList<SuiteRunDetails>?> Handle(ListSuiteRuns request, CancellationToken cancellationToken)
    {
        if (await TestSuiteKeys.FindAsync(suites, request.Key, cancellationToken) is not { } suite)
        {
            return null;
        }

        var scenariosById = await SuiteRunDetailsFactory.ScenariosByIdAsync(scenarios, cancellationToken);
        var details = new List<SuiteRunDetails>();
        foreach (var run in await suiteRuns.ListAsync(suite.Id, Math.Clamp(request.Limit, 1, MaxLimit), cancellationToken))
        {
            details.Add(await SuiteRunDetailsFactory.BuildAsync(run, suite, runs, scenariosById, cancellationToken));
        }

        return details;
    }
}
