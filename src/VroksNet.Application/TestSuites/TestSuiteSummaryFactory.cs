using VroksNet.Application.Abstractions;
using VroksNet.Domain.TestScenarios;
using VroksNet.Domain.TestSuites;

namespace VroksNet.Application.TestSuites;

internal static class TestSuiteSummaryFactory
{
    public static async Task<TestSuiteSummary> BuildAsync(
        TestSuite suite, ISuiteRunRepository suiteRuns, ITestRunRepository runs, IReadOnlyDictionary<Guid, TestScenario> scenariosById, CancellationToken cancellationToken)
    {
        var scenarios = suite.TestScenarioIds
            .Select(id => scenariosById.GetValueOrDefault(id) is { } scenario
                ? new TestSuiteScenario(id, scenario.Name, scenario.Kind)
                : new TestSuiteScenario(id, SuiteRunDetailsFactory.DeletedScenarioName, null))
            .ToList();
        var latest = (await suiteRuns.ListAsync(suite.Id, 1, cancellationToken)).FirstOrDefault();
        var lastRun = latest is null ? null : await SuiteRunDetailsFactory.BuildAsync(latest, suite, runs, scenariosById, cancellationToken);
        return new TestSuiteSummary(suite.Id, suite.Name, scenarios, suite.RunOnStartup, suite.UpdatedAt, suite.ProvisionedAt, lastRun);
    }
}
