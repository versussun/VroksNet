using Mediator;
using VroksNet.Application.Abstractions;
using VroksNet.Domain.TestRuns;

namespace VroksNet.Application.TestRuns.StartTestRun;

public sealed class StartTestRunHandler(
    ITestScenarioRepository scenarios,
    ITestRunRepository runs,
    TimeProvider timeProvider) : IRequestHandler<StartTestRun, Guid?>
{
    /// <summary>How far ahead a delayed run can be queued.</summary>
    public static readonly TimeSpan MaxDelay = TimeSpan.FromDays(30);

    public async ValueTask<Guid?> Handle(StartTestRun request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var scheduledFor = When(request, now);

        if (await scenarios.FindByIdAsync(request.TestScenarioId, cancellationToken) is null)
        {
            return null;
        }

        var run = new TestRun
        {
            Id = Guid.NewGuid(),
            TestScenarioId = request.TestScenarioId,
            Status = TestRunStatus.Queued,
            Trigger = scheduledFor is null ? TestRunTrigger.Manual : TestRunTrigger.Delayed,
            ScheduledFor = scheduledFor ?? now
        };
        await runs.InsertAsync(run, cancellationToken);
        return run.Id;
    }

    /// <summary>When a delayed run should start — null for an immediate one. Invalid input throws <see cref="ArgumentException"/> (the endpoint's 400).</summary>
    private static DateTimeOffset? When(StartTestRun request, DateTimeOffset now)
    {
        switch (request)
        {
            case { RunAt: not null, DelaySeconds: not null }:
                throw new ArgumentException("Set runAt or delaySeconds, not both.");
            case { DelaySeconds: { } seconds } when seconds < 1 || seconds > MaxDelay.TotalSeconds:
                throw new ArgumentException($"delaySeconds must be from 1 to {MaxDelay.TotalSeconds:0} ({MaxDelay.TotalDays:0} days).");
            case { DelaySeconds: { } seconds }:
                return now.AddSeconds(seconds);
            case { RunAt: { } runAt } when runAt <= now:
                throw new ArgumentException($"runAt ({runAt:O}) has already passed; leave it out to run now.");
            case { RunAt: { } runAt } when runAt - now > MaxDelay:
                throw new ArgumentException($"runAt can be at most {MaxDelay.TotalDays:0} days ahead.");
            case { RunAt: { } runAt }:
                return runAt.ToUniversalTime();
            default:
                return null;
        }
    }
}
