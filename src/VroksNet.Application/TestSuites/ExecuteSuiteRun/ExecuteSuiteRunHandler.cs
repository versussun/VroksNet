using Mediator;
using VroksNet.Application.Abstractions;
using VroksNet.Application.TestRuns;
using VroksNet.Domain.TestRuns;
using VroksNet.Domain.TestScenarios;
using VroksNet.Domain.TestSuites;

namespace VroksNet.Application.TestSuites.ExecuteSuiteRun;

/// <summary>
/// Runs a suite (ADR 0002, "Suites"): a <see cref="TestRun"/> per scenario, each through
/// <see cref="TestRunRunner"/> like any other run. <b>Every Listen starts first</b>, and the Sends
/// only once each Listen is subscribed (or has failed, or <see cref="ListenReadyTimeout"/> passed),
/// so a suite checks a whole chain — "sent a command → the service handled it → published an
/// event → we caught it" — without missing the event. Sends then run one after another in the
/// suite's order, and the suite waits for the Listens to finish. It passes only if every run
/// passed; a scenario deleted since the suite was saved fails its run.
/// Cancelling the suite run cancels its runs; shutdown interrupts them.
/// </summary>
public sealed class ExecuteSuiteRunHandler(
    ISuiteRunRepository suiteRuns,
    ITestSuiteRepository suites,
    ITestScenarioRepository scenarios,
    ITestRunRepository runs,
    TestRunRunner runner,
    TestRunCancellations cancellations,
    TimeProvider timeProvider) : IRequestHandler<ExecuteSuiteRun>
{
    /// <summary>The longest the Sends wait for the Listens to subscribe — past the listener's own connect and setup budgets (10 s each).</summary>
    public static readonly TimeSpan ListenReadyTimeout = TimeSpan.FromSeconds(30);

    public async ValueTask<Unit> Handle(ExecuteSuiteRun request, CancellationToken cancellationToken)
    {
        var suiteRun = await suiteRuns.FindByIdAsync(request.SuiteRunId, cancellationToken);
        if (suiteRun is null || !await suiteRuns.MarkRunningAsync(suiteRun.Id, timeProvider.GetUtcNow(), cancellationToken))
        {
            return Unit.Value;
        }

        var token = cancellations.Register(suiteRun.Id, cancellationToken);
        var listens = new List<Task>();
        try
        {
            var suite = await suites.FindByIdAsync(suiteRun.TestSuiteId, token);
            if (suite is null)
            {
                await suiteRuns.CompleteAsync(suiteRun.Id, TestRunStatus.Failed, timeProvider.GetUtcNow(), "The suite no longer exists.", CancellationToken.None);
                return Unit.Value;
            }

            var members = await QueueMembersAsync(suiteRun, suite, token);
            var scenariosById = (await scenarios.ListAsync(token)).ToDictionary(scenario => scenario.Id);
            bool IsListen(TestRun member) => scenariosById.GetValueOrDefault(member.TestScenarioId)?.Kind == TestScenarioKind.Listen;

            var ready = new List<Task>();
            foreach (var member in members.Where(IsListen))
            {
                var subscribed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                ready.Add(subscribed.Task);
                listens.Add(ListenAsync(member, subscribed, suiteRun.Id, token));
            }

            try
            {
                await Task.WhenAll(ready).WaitAsync(ListenReadyTimeout, timeProvider, token);
            }
            catch (TimeoutException)
            {
                // A Listen still setting up after this long will fail on its own; send anyway.
            }

            // The runner records a cancelled run and returns, so the suite checks the token itself:
            // a cancelled suite run leaves its remaining runs unstarted and ends as Cancelled.
            foreach (var member in members.Where(member => !IsListen(member)))
            {
                token.ThrowIfCancellationRequested();
                await runner.RunAsync(member, token, parentId: suiteRun.Id);
            }

            await Task.WhenAll(listens);
            token.ThrowIfCancellationRequested();

            var finished = await runs.ListBySuiteRunAsync(suiteRun.Id, CancellationToken.None);
            var failed = finished.Where(member => member.Status != TestRunStatus.Passed)
                .Select(member => scenariosById.GetValueOrDefault(member.TestScenarioId)?.Name ?? SuiteRunDetailsFactory.DeletedScenarioName)
                .ToList();
            var (status, message) = failed.Count == 0
                ? (TestRunStatus.Passed, $"All {finished.Count} passed.")
                : (TestRunStatus.Failed, $"{finished.Count - failed.Count} of {finished.Count} passed. Failed: {string.Join(", ", failed)}.");
            await suiteRuns.CompleteAsync(suiteRun.Id, status, timeProvider.GetUtcNow(), message, CancellationToken.None);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            await WhenAllQuietlyAsync(listens);
            var cancelled = cancellations.WasCancelled(suiteRun.Id);
            var at = timeProvider.GetUtcNow();
            await runs.CancelQueuedBySuiteRunAsync(suiteRun.Id, at, cancelled ? "Cancelled with its suite run." : "Not started: the app was shutting down.", CancellationToken.None);
            await suiteRuns.CompleteAsync(
                suiteRun.Id,
                cancelled ? TestRunStatus.Cancelled : TestRunStatus.Interrupted,
                at,
                cancelled ? "Cancelled." : "Interrupted: the app was shutting down.",
                CancellationToken.None);
        }
        catch (Exception)
        {
            await WhenAllQuietlyAsync(listens);
            await runs.CancelQueuedBySuiteRunAsync(suiteRun.Id, timeProvider.GetUtcNow(), "Not started: the suite run failed.", CancellationToken.None);
            await suiteRuns.CompleteAsync(suiteRun.Id, TestRunStatus.Failed, timeProvider.GetUtcNow(), "The suite run failed unexpectedly; see the app log.", CancellationToken.None);
            throw;
        }
        finally
        {
            cancellations.Unregister(suiteRun.Id);
        }

        return Unit.Value;
    }

    /// <summary>One queued run per scenario, a tick apart so the history keeps the suite's order.</summary>
    private async Task<List<TestRun>> QueueMembersAsync(SuiteRun suiteRun, TestSuite suite, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var members = suite.TestScenarioIds.Select((scenarioId, index) => new TestRun
        {
            Id = Guid.NewGuid(),
            TestScenarioId = scenarioId,
            SuiteRunId = suiteRun.Id,
            Status = TestRunStatus.Queued,
            Trigger = TestRunTrigger.Suite,
            ScheduledFor = now.AddTicks(index)
        }).ToList();

        foreach (var member in members)
        {
            await runs.InsertAsync(member, cancellationToken);
        }

        return members;
    }

    private async Task ListenAsync(TestRun member, TaskCompletionSource subscribed, Guid suiteRunId, CancellationToken cancellationToken)
    {
        try
        {
            await runner.RunAsync(member, cancellationToken, onListening: () => subscribed.TrySetResult(), parentId: suiteRunId);
        }
        finally
        {
            // Failed before subscribing (or never started): nothing to wait for.
            subscribed.TrySetResult();
        }
    }

    private static async Task WhenAllQuietlyAsync(List<Task> tasks)
    {
        try
        {
            await Task.WhenAll(tasks);
        }
        catch (Exception)
        {
            // Each run already recorded how it ended.
        }
    }
}
