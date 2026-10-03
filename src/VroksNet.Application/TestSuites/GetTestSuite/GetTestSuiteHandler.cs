using Mediator;
using VroksNet.Application.Abstractions;

namespace VroksNet.Application.TestSuites.GetTestSuite;

public sealed class GetTestSuiteHandler(
    ITestSuiteRepository suites,
    ISuiteRunRepository suiteRuns,
    ITestScenarioRepository scenarios,
    ITestRunRepository runs) : IRequestHandler<GetTestSuite, TestSuiteSummary?>
{
    public async ValueTask<TestSuiteSummary?> Handle(GetTestSuite request, CancellationToken cancellationToken)
    {
        var suite = await TestSuiteKeys.FindAsync(suites, request.Key, cancellationToken);
        return suite is null
            ? null
            : await TestSuiteSummaryFactory.BuildAsync(suite, suiteRuns, runs, await SuiteRunDetailsFactory.ScenariosByIdAsync(scenarios, cancellationToken), cancellationToken);
    }
}
