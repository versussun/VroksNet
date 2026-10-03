using Mediator;
using VroksNet.Application.Abstractions;

namespace VroksNet.Application.TestSuites.GetSuiteRun;

public sealed class GetSuiteRunHandler(
    ISuiteRunRepository suiteRuns,
    ITestSuiteRepository suites,
    ITestScenarioRepository scenarios,
    ITestRunRepository runs) : IRequestHandler<GetSuiteRun, SuiteRunDetails?>
{
    public async ValueTask<SuiteRunDetails?> Handle(GetSuiteRun request, CancellationToken cancellationToken)
    {
        if (await suiteRuns.FindByIdAsync(request.Id, cancellationToken) is not { } run)
        {
            return null;
        }

        var suite = await suites.FindByIdAsync(run.TestSuiteId, cancellationToken);
        return await SuiteRunDetailsFactory.BuildAsync(run, suite, runs, await SuiteRunDetailsFactory.ScenariosByIdAsync(scenarios, cancellationToken), cancellationToken);
    }
}
