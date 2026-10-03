using VroksNet.Application.Abstractions;
using VroksNet.Domain.TestRuns;
using VroksNet.Domain.TestScenarios;
using VroksNet.Domain.TestSuites;

namespace VroksNet.Application.TestSuites;

/// <summary>Builds <see cref="SuiteRunDetails"/> — shared by every handler that returns a suite run.</summary>
internal static class SuiteRunDetailsFactory
{
    public const string DeletedScenarioName = "(deleted scenario)";

    public static async Task<SuiteRunDetails> BuildAsync(
        SuiteRun run, TestSuite? suite, ITestRunRepository runs, IReadOnlyDictionary<Guid, TestScenario> scenariosById, CancellationToken cancellationToken)
    {
        var members = await runs.ListBySuiteRunAsync(run.Id, cancellationToken);
        var entries = members.Count > 0
            ? members.Select(member => Entry(member.TestScenarioId, member.Id, member.Status, member.Message, scenariosById)).ToList()
            // Not started yet: the suite's scenarios, all waiting.
            : (suite?.TestScenarioIds ?? []).Select(id => Entry(id, null, TestRunStatus.Queued, null, scenariosById)).ToList();

        var failed = run.IsFinished
            ? entries.Where(entry => entry.Status != TestRunStatus.Passed).Select(entry => entry.ScenarioName).ToList()
            : [];

        return new SuiteRunDetails(
            run.Id, run.TestSuiteId, suite?.Name ?? "(deleted suite)", run.Status, run.Trigger,
            run.ScheduledFor, run.StartedAt, run.FinishedAt, run.Message, entries, failed);
    }

    public static async Task<IReadOnlyDictionary<Guid, TestScenario>> ScenariosByIdAsync(ITestScenarioRepository scenarios, CancellationToken cancellationToken)
        => (await scenarios.ListAsync(cancellationToken)).ToDictionary(scenario => scenario.Id);

    private static SuiteRunEntry Entry(Guid scenarioId, Guid? runId, TestRunStatus status, string? message, IReadOnlyDictionary<Guid, TestScenario> scenariosById)
    {
        var scenario = scenariosById.GetValueOrDefault(scenarioId);
        return new SuiteRunEntry(scenarioId, scenario?.Name ?? DeletedScenarioName, scenario?.Kind, runId, status, message);
    }
}
