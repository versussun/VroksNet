using Mediator;
using VroksNet.Application.Abstractions;
using VroksNet.Application.TestRuns;
using VroksNet.Domain.TestRuns;
using VroksNet.Domain.TestScenarios;

namespace VroksNet.Application.TestScenarios.RunTestScenario;

/// <summary>
/// Runs a scenario synchronously: the caller's request stays open until the run finishes, and gets
/// its result. The run is recorded as a <see cref="TestRun"/> (<see cref="TestRunTrigger.Manual"/>)
/// like a background one, and can be cancelled the same way. The run itself is
/// <see cref="TestScenarioExecutor"/>, shared with background runs.
/// <para>
/// A Listen scenario waiting longer than <see cref="TestScenarioListening.MaxSynchronousTimeoutSeconds"/>
/// can't run synchronously — the request would outlive the Admin UI's HTTP timeout — so it's
/// refused without running and pointed at a background run.
/// </para>
/// </summary>
public sealed class RunTestScenarioHandler(
    ITestScenarioRepository scenarios,
    ITestRunRepository runs,
    TestScenarioExecutor executor,
    TestRunCancellations cancellations,
    TimeProvider timeProvider) : IRequestHandler<RunTestScenario, RunTestScenarioResult?>
{
    public async ValueTask<RunTestScenarioResult?> Handle(RunTestScenario request, CancellationToken cancellationToken)
    {
        var scenario = await scenarios.FindByIdAsync(request.Id, cancellationToken);
        if (scenario is null)
        {
            return null;
        }

        if (TestScenarioListening.RequiresBackgroundRun(scenario.Kind, scenario.ListenTimeoutSeconds))
        {
            return new RunTestScenarioResult(
                false,
                $"This scenario waits up to {scenario.ListenTimeoutSeconds}s for a message, longer than a synchronous run allows ({TestScenarioListening.MaxSynchronousTimeoutSeconds}s). Run it in the background.",
                null);
        }

        var now = timeProvider.GetUtcNow();
        var run = new TestRun
        {
            Id = Guid.NewGuid(),
            TestScenarioId = scenario.Id,
            Status = TestRunStatus.Running,
            Trigger = TestRunTrigger.Manual,
            ScheduledFor = now,
            StartedAt = now
        };
        await runs.InsertAsync(run, cancellationToken);

        var token = cancellations.Register(run.Id, cancellationToken);
        try
        {
            var result = await executor.ExecuteAsync(scenario, run.Id, token);
            await runs.CompleteAsync(run.Id, TestScenarioExecutor.OutcomeOf(result, timeProvider.GetUtcNow()), CancellationToken.None);
            return result;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Cancelled through the API, or the caller went away (the Admin UI's Stop button).
            await runs.CompleteAsync(run.Id, new TestRunOutcome(TestRunStatus.Cancelled, timeProvider.GetUtcNow(), "Cancelled."), CancellationToken.None);
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return new RunTestScenarioResult(false, "Cancelled.", null);
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
    }
}
