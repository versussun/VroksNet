using Mediator;
using VroksNet.Application.Abstractions;
using VroksNet.Application.TestScenarios;
using VroksNet.Domain.TestRuns;

namespace VroksNet.Application.TestRuns.ExecuteTestRun;

/// <summary>
/// Moves a run Queued → Running, executes its scenario through <see cref="TestScenarioExecutor"/>
/// (the same code as a synchronous run) and records the outcome. A cancellation ends it as
/// <see cref="TestRunStatus.Cancelled"/> when someone asked for it, otherwise as
/// <see cref="TestRunStatus.Interrupted"/> (the app is shutting down). Whatever happens, the run
/// never stays Running.
/// </summary>
public sealed class ExecuteTestRunHandler(
    ITestRunRepository runs,
    ITestScenarioRepository scenarios,
    TestScenarioExecutor executor,
    TestRunCancellations cancellations,
    TimeProvider timeProvider) : IRequestHandler<ExecuteTestRun>
{
    public async ValueTask<Unit> Handle(ExecuteTestRun request, CancellationToken cancellationToken)
    {
        var run = await runs.FindByIdAsync(request.RunId, cancellationToken);
        if (run is null || !await runs.MarkRunningAsync(run.Id, timeProvider.GetUtcNow(), cancellationToken))
        {
            return Unit.Value;
        }

        var token = cancellations.Register(run.Id, cancellationToken);
        try
        {
            var scenario = await scenarios.FindByIdAsync(run.TestScenarioId, token);
            if (scenario is null)
            {
                await runs.CompleteAsync(run.Id, new TestRunOutcome(TestRunStatus.Failed, timeProvider.GetUtcNow(), "The scenario no longer exists."), CancellationToken.None);
                return Unit.Value;
            }

            var result = await executor.ExecuteAsync(scenario, run.Id, token);
            await runs.CompleteAsync(run.Id, TestScenarioExecutor.OutcomeOf(result, timeProvider.GetUtcNow()), CancellationToken.None);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            var outcome = cancellations.WasCancelled(run.Id)
                ? new TestRunOutcome(TestRunStatus.Cancelled, timeProvider.GetUtcNow(), "Cancelled.")
                : new TestRunOutcome(TestRunStatus.Interrupted, timeProvider.GetUtcNow(), "Interrupted: the app was shutting down.");
            await runs.CompleteAsync(run.Id, outcome, CancellationToken.None);
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

        return Unit.Value;
    }
}
