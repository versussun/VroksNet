using System.Globalization;
using VroksNet.Application.Abstractions;
using VroksNet.Application.TestRuns.QueueScheduledTestRuns;
using VroksNet.Application.TestRuns.SkipMissedScheduledTestRuns;
using VroksNet.Domain.TestRuns;
using VroksNet.Domain.TestScenarios;
using VroksNet.Infrastructure.Scheduling;
using VroksNet.UnitTests.TestDoubles;

namespace VroksNet.UnitTests.TestRuns;

/// <summary>
/// Cron scheduling (ADR 0002, step B2): the planner the worker calls every tick, driven second by
/// second with a stand-in for the worker that starts and finishes each due run.
/// </summary>
public sealed class ScheduledTestRunsTests
{
    private readonly FakeTestScenarioRepository _scenarios = new();
    private readonly FakeTestRunRepository _runs = new();
    private readonly QueueScheduledTestRunsHandler _queue;

    public ScheduledTestRunsTests() => _queue = new QueueScheduledTestRunsHandler(_scenarios, _runs, new CronSchedule());

    [Fact]
    public async Task EveryMinute_RunsOnceAMinute_OnTheMinute()
    {
        var scenario = await AddScenarioAsync("*/1 * * * *");

        await TickAsync(At("2026-10-03T10:00:30Z"), At("2026-10-03T10:03:30Z"));

        var scheduled = ScheduledRuns(scenario);
        Assert.Equal([At("2026-10-03T10:01Z"), At("2026-10-03T10:02Z"), At("2026-10-03T10:03Z"), At("2026-10-03T10:04Z")], scheduled.Select(run => run.ScheduledFor));
        Assert.All(scheduled.SkipLast(1), run => Assert.Equal(TestRunStatus.Passed, run.Status));
        Assert.Equal(TestRunStatus.Queued, scheduled[^1].Status); // the next one, already visible in the history
    }

    [Fact]
    public async Task OnlyOneRunIsQueuedAtATime()
    {
        var scenario = await AddScenarioAsync("0 9 * * *", "Europe/Kyiv");

        Assert.Equal(1, await QueueAsync(At("2026-10-03T10:00Z")));
        Assert.Equal(0, await QueueAsync(At("2026-10-03T10:00:01Z")));

        Assert.Equal(At("2026-10-04T06:00Z"), Assert.Single(ScheduledRuns(scenario)).ScheduledFor); // 09:00 EEST
    }

    [Fact]
    public async Task AfterDowntime_MissedRunsArentCaughtUp()
    {
        var scenario = await AddScenarioAsync("*/1 * * * *");
        await TickAsync(At("2026-10-03T10:00:30Z"), At("2026-10-03T10:01:30Z")); // ran 10:01; 10:02 is queued

        // The app is down from 10:01:30 to 10:30:20; at startup the queued 10:02 is skipped…
        var skipped = await new SkipMissedScheduledTestRunsHandler(_runs, new FixedTimeProvider(At("2026-10-03T10:30:20Z")))
            .Handle(new SkipMissedScheduledTestRuns(), TestContext.Current.CancellationToken);
        Assert.Equal(1, skipped);

        // …and the next run is the first occurrence from now, not 10:03.
        await QueueAsync(At("2026-10-03T10:30:21Z"));
        var scheduled = ScheduledRuns(scenario);
        Assert.Equal(TestRunStatus.Cancelled, scheduled.Single(run => run.ScheduledFor == At("2026-10-03T10:02Z")).Status);
        Assert.Equal(At("2026-10-03T10:31Z"), scheduled.Single(run => run.Status == TestRunStatus.Queued).ScheduledFor);
    }

    [Fact]
    public async Task ARunThatWaitedPastTheNextOccurrence_DoesntCauseABurst()
    {
        var scenario = await AddScenarioAsync("*/1 * * * *");
        await QueueAsync(At("2026-10-03T10:00:30Z")); // 10:01

        // It only gets to run at 10:04:10 (say, behind a long run of the same scenario).
        var run = ScheduledRuns(scenario).Single();
        await StartAndFinishAsync(run, At("2026-10-03T10:04:10Z"));
        await QueueAsync(At("2026-10-03T10:04:11Z"));

        Assert.Equal(At("2026-10-03T10:05Z"), ScheduledRuns(scenario).Single(r => r.Status == TestRunStatus.Queued).ScheduledFor);
    }

    [Fact]
    public async Task AnOccurrenceAtExactlyNow_IsntSkipped()
    {
        var scenario = await AddScenarioAsync("*/1 * * * *");
        await _runs.InsertAsync(new TestRun
        {
            Id = Guid.NewGuid(),
            TestScenarioId = scenario.Id,
            Status = TestRunStatus.Passed,
            Trigger = TestRunTrigger.Schedule,
            ScheduledFor = At("2026-10-03T10:05Z")
        }, TestContext.Current.CancellationToken);

        await QueueAsync(At("2026-10-03T10:31Z"));

        Assert.Equal(At("2026-10-03T10:31Z"), ScheduledRuns(scenario).Single(r => r.Status == TestRunStatus.Queued).ScheduledFor);
    }

    [Fact]
    public async Task ScenariosWithoutASchedule_ArentQueued()
    {
        await AddScenarioAsync(null);

        Assert.Equal(0, await QueueAsync(At("2026-10-03T10:00Z")));
        Assert.Empty(_runs.All);
    }

    private async Task<TestScenario> AddScenarioAsync(string? schedule, string? timeZone = null)
    {
        var scenario = new TestScenario { Id = Guid.NewGuid(), Name = $"scheduled-{Guid.NewGuid():N}", Schedule = schedule, ScheduleTimeZone = timeZone };
        await _scenarios.InsertAsync(scenario, TestContext.Current.CancellationToken);
        return scenario;
    }

    private async Task<int> QueueAsync(DateTimeOffset now)
        => await _queue.Handle(new QueueScheduledTestRuns(now), TestContext.Current.CancellationToken);

    /// <summary>Every second from <paramref name="from"/> to <paramref name="to"/>, what the worker does: queue scheduled runs, then start and finish each due one.</summary>
    private async Task TickAsync(DateTimeOffset from, DateTimeOffset to)
    {
        for (var now = from; now <= to; now = now.AddSeconds(1))
        {
            await QueueAsync(now);
            foreach (var due in await _runs.ListDueAsync(now, TestContext.Current.CancellationToken))
            {
                await StartAndFinishAsync(due, now);
            }
        }
    }

    private async Task StartAndFinishAsync(TestRun run, DateTimeOffset at)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Assert.True(await _runs.MarkRunningAsync(run.Id, at, cancellationToken));
        Assert.True(await _runs.CompleteAsync(run.Id, new TestRunOutcome(TestRunStatus.Passed, at.AddMilliseconds(200), "200 OK"), cancellationToken));
    }

    private List<TestRun> ScheduledRuns(TestScenario scenario)
        => _runs.All.Where(run => run.TestScenarioId == scenario.Id && run.Trigger == TestRunTrigger.Schedule).OrderBy(run => run.ScheduledFor).ToList();

    private static DateTimeOffset At(string utc) => DateTimeOffset.Parse(utc, CultureInfo.InvariantCulture);
}
