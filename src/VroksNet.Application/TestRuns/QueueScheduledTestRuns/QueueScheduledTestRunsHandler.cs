using Mediator;
using VroksNet.Application.Abstractions;
using VroksNet.Domain.TestRuns;

namespace VroksNet.Application.TestRuns.QueueScheduledTestRuns;

/// <summary>
/// The next run is the cron's next occurrence after the previous scheduled run's
/// <see cref="TestRun.ScheduledFor"/> — its planned time, not when it actually started, so the
/// schedule doesn't drift with the worker's tick or with queueing. If that occurrence has already
/// passed (the app was down, or the previous run waited behind others), the missed ones aren't
/// caught up: the next is the first occurrence from now on. A queued scheduled run is therefore
/// always the scenario's next one, which the history shows.
/// </summary>
public sealed class QueueScheduledTestRunsHandler(
    ITestScenarioRepository scenarios,
    ITestRunRepository runs,
    ICronSchedule cron) : IRequestHandler<QueueScheduledTestRuns, int>
{
    public async ValueTask<int> Handle(QueueScheduledTestRuns request, CancellationToken cancellationToken)
    {
        var queued = 0;
        foreach (var scenario in await scenarios.ListAsync(cancellationToken))
        {
            if (scenario.Schedule is not { } schedule)
            {
                continue;
            }

            var latest = await runs.FindLatestScheduledAsync(scenario.Id, cancellationToken);
            if (latest?.Status == TestRunStatus.Queued)
            {
                continue;
            }

            var next = latest is null
                ? cron.GetNextOccurrence(schedule, scenario.ScheduleTimeZone, request.Now)
                : cron.GetNextOccurrence(schedule, scenario.ScheduleTimeZone, latest.ScheduledFor);
            if (next < request.Now)
            {
                next = cron.GetNextOccurrence(schedule, scenario.ScheduleTimeZone, request.Now, inclusive: true);
            }

            // Null: the expression never occurs again, or stopped being valid (say, a time zone
            // the system no longer knows) — nothing to queue.
            if (next is not { } scheduledFor)
            {
                continue;
            }

            await runs.InsertAsync(new TestRun
            {
                Id = Guid.NewGuid(),
                TestScenarioId = scenario.Id,
                Status = TestRunStatus.Queued,
                Trigger = TestRunTrigger.Schedule,
                ScheduledFor = scheduledFor
            }, cancellationToken);
            queued++;
        }

        return queued;
    }
}
