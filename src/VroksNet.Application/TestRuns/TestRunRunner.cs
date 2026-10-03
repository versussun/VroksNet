using VroksNet.Application.Abstractions;
using VroksNet.Application.TestScenarios;
using VroksNet.Domain.TestRuns;

namespace VroksNet.Application.TestRuns;

/// <summary>
/// One queued <see cref="TestRun"/>'s lifecycle: Queued → Running, its scenario through
/// <see cref="TestScenarioExecutor"/> (the same code as a synchronous run), then the outcome.
/// Shared by <c>ExecuteTestRun</c> (a run on its own) and <c>ExecuteSuiteRun</c> (a suite's runs).
/// A cancellation ends it as <see cref="TestRunStatus.Cancelled"/> when someone asked for it — the
/// run itself or, for a suite's run, the suite run (<c>parentId</c>) — otherwise as
/// <see cref="TestRunStatus.Interrupted"/> (the app is shutting down). Whatever happens, the run
/// never stays Running.
/// </summary>
public sealed class TestRunRunner(
    ITestRunRepository runs,
    ITestScenarioRepository scenarios,
    TestScenarioExecutor executor,
    TestRunCancellations cancellations,
    TimeProvider timeProvider)
{
    /// <returns>The run's final status, or null if it couldn't be started (gone, or no longer queued).</returns>
    public async Task<TestRunStatus?> RunAsync(TestRun run, CancellationToken cancellationToken, Action? onListening = null, Guid? parentId = null)
    {
        if (!await runs.MarkRunningAsync(run.Id, timeProvider.GetUtcNow(), cancellationToken))
        {
            return null;
        }

        var token = cancellations.Register(run.Id, cancellationToken);
        TestRunOutcome outcome;
        try
        {
            var scenario = await scenarios.FindByIdAsync(run.TestScenarioId, token);
            if (scenario is null)
            {
                outcome = new TestRunOutcome(TestRunStatus.Failed, timeProvider.GetUtcNow(), "The scenario no longer exists.");
            }
            else
            {
                var result = await executor.ExecuteAsync(scenario, run.Id, token, onListening);
                outcome = TestScenarioExecutor.OutcomeOf(result, timeProvider.GetUtcNow());
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            outcome = cancellations.WasCancelled(run.Id) || (parentId is { } parent && cancellations.WasCancelled(parent))
                ? new TestRunOutcome(TestRunStatus.Cancelled, timeProvider.GetUtcNow(), "Cancelled.")
                : new TestRunOutcome(TestRunStatus.Interrupted, timeProvider.GetUtcNow(), "Interrupted: the app was shutting down.");
        }
        catch (Exception)
        {
            await runs.CompleteAsync(run.Id, new TestRunOutcome(TestRunStatus.Failed, timeProvider.GetUtcNow(), "The run failed unexpectedly; see the app log."), CancellationToken.None);
            throw;
        }
        finally
        {
            cancellations.Unregister(run.Id);
        }

        await runs.CompleteAsync(run.Id, outcome, CancellationToken.None);
        return outcome.Status;
    }
}
