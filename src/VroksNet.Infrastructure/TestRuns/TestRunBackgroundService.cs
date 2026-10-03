using Mediator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VroksNet.Application.TestRuns.ExecuteTestRun;
using VroksNet.Application.TestRuns.InterruptRunningTestRuns;
using VroksNet.Application.TestRuns.ListDueTestRuns;
using VroksNet.Application.TestRuns.PruneTestRunHistory;
using VroksNet.Application.TestRuns.QueueScheduledTestRuns;
using VroksNet.Application.TestRuns.SkipMissedScheduledTestRuns;
using VroksNet.Application.TestSuites.ExecuteSuiteRun;
using VroksNet.Application.TestSuites.ListDueSuiteRuns;

namespace VroksNet.Infrastructure.TestRuns;

/// <summary>
/// The background test-run worker (ADR 0002). Like <c>PublisherBackgroundService</c> it only
/// schedules: once a second it asks Application which queued runs are due and starts each through
/// <see cref="ExecuteTestRun"/>, where the run logic lives. Unlike it, a tick doesn't wait for the
/// runs it started — a Listen run can wait up to 30 minutes — so it tracks them itself:
/// <list type="bullet">
/// <item>at most one run per scenario at a time, and at most <c>TestRuns:MaxConcurrency</c> (4) in all;</item>
/// <item>suite runs too (<see cref="ExecuteSuiteRun"/>): one per suite at a time, each taking one slot
/// of that limit — its own runs are started by the suite run, not by this loop;</item>
/// <item>at startup, runs left Running by the previous process become Interrupted, and scheduled runs
/// missed while it was down are skipped;</item>
/// <item>every tick, scheduled scenarios get their next run queued (<see cref="QueueScheduledTestRuns"/>);</item>
/// <item>every few minutes, history beyond <c>TestRuns:RetentionPerScenario</c> (100) per scenario is deleted;</item>
/// <item>on shutdown, running runs are cancelled (ending as Interrupted) and awaited.</item>
/// </list>
/// A failing tick or run is logged and the loop carries on.
/// </summary>
public sealed class TestRunBackgroundService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IConfiguration configuration,
    ILogger<TestRunBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PruneEvery = TimeSpan.FromMinutes(5);

    private readonly int _maxConcurrency = Math.Max(1, configuration.GetValue("TestRuns:MaxConcurrency", 4));
    private readonly int _retentionPerScenario = Math.Max(1, configuration.GetValue("TestRuns:RetentionPerScenario", 100));

    /// <summary>
    /// The runs and suite runs this worker started that haven't finished, by run id, with their
    /// owner (the scenario or the suite) to keep one per owner; touched only by the loop.
    /// </summary>
    private readonly Dictionary<Guid, (Guid OwnerId, Task Execution)> _running = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await SendSafelyAsync(new InterruptRunningTestRuns(), "Marking runs left over from the last shutdown as interrupted", stoppingToken);
        await SendSafelyAsync(new SkipMissedScheduledTestRuns(), "Skipping scheduled runs missed while the app was down", stoppingToken);
        var lastPrune = DateTimeOffset.MinValue;

        using var timer = new PeriodicTimer(Tick, timeProvider);
        try
        {
            do
            {
                try
                {
                    await SendSafelyAsync(new QueueScheduledTestRuns(timeProvider.GetUtcNow()), "Queueing scheduled runs", stoppingToken, LogLevel.Debug);
                    await StartDueRunsAsync(stoppingToken);

                    if (timeProvider.GetUtcNow() - lastPrune >= PruneEvery)
                    {
                        lastPrune = timeProvider.GetUtcNow();
                        await SendSafelyAsync(new PruneTestRunHistory(_retentionPerScenario), "Pruning the test-run history", stoppingToken);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Starting the due test runs failed.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down: the runs see the same token, end as Interrupted, and are awaited below.
        }

        await Task.WhenAll(_running.Values.Select(entry => entry.Execution));
    }

    private async Task StartDueRunsAsync(CancellationToken stoppingToken)
    {
        foreach (var finished in _running.Where(entry => entry.Value.Execution.IsCompleted).Select(entry => entry.Key).ToList())
        {
            _running.Remove(finished);
        }

        if (_running.Count >= _maxConcurrency)
        {
            return;
        }

        IReadOnlyList<DueTestRun> due;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            due = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new ListDueTestRuns(timeProvider.GetUtcNow()), stoppingToken);
        }

        foreach (var run in due)
        {
            if (_running.Count >= _maxConcurrency)
            {
                break;
            }

            // Already started (it stays Queued until its execution marks it Running), or its
            // scenario is busy — it stays queued and is picked up on a later tick.
            if (_running.ContainsKey(run.RunId) || _running.Values.Any(entry => entry.OwnerId == run.TestScenarioId))
            {
                continue;
            }

            _running[run.RunId] = (run.TestScenarioId, Task.Run(() => ExecuteAsync(new ExecuteTestRun(run.RunId), "Test run", run.RunId, stoppingToken), CancellationToken.None));
        }

        if (_running.Count >= _maxConcurrency)
        {
            return;
        }

        IReadOnlyList<DueSuiteRun> dueSuites;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            dueSuites = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new ListDueSuiteRuns(timeProvider.GetUtcNow()), stoppingToken);
        }

        foreach (var suiteRun in dueSuites)
        {
            if (_running.Count >= _maxConcurrency)
            {
                break;
            }

            if (_running.ContainsKey(suiteRun.SuiteRunId) || _running.Values.Any(entry => entry.OwnerId == suiteRun.TestSuiteId))
            {
                continue;
            }

            _running[suiteRun.SuiteRunId] = (suiteRun.TestSuiteId, Task.Run(() => ExecuteAsync(new ExecuteSuiteRun(suiteRun.SuiteRunId), "Suite run", suiteRun.SuiteRunId, stoppingToken), CancellationToken.None));
        }
    }

    private async Task ExecuteAsync(IRequest request, string what, Guid runId, CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IMediator>().Send(request, stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "{What} {RunId} failed.", what, runId);
        }
        catch (OperationCanceledException)
        {
            // Shutdown; the run already recorded itself as Interrupted.
        }
    }

    private async Task SendSafelyAsync(IRequest<int> request, string what, CancellationToken stoppingToken, LogLevel level = LogLevel.Information)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var affected = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(request, stoppingToken);
            if (affected > 0)
            {
                logger.Log(level, "{What}: {Count} run(s).", what, affected);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "{What} failed.", what);
        }
    }
}
