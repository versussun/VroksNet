using System.Text.Json;
using VroksNet.Domain.TestRuns;

namespace VroksNet.Application.TestRuns;

internal static class TestRunSummaries
{
    public static TestRunSummary From(TestRun run) => new(
        run.Id,
        run.TestScenarioId,
        run.Status,
        run.Trigger,
        run.ScheduledFor,
        run.StartedAt,
        run.FinishedAt,
        run.Message,
        run.StatusCode,
        run.ContractValid,
        run.ValidationErrors is null ? [] : JsonSerializer.Deserialize<string[]>(run.ValidationErrors) ?? []);
}
