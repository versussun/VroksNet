using Mediator;
using VroksNet.Application.Abstractions;

namespace VroksNet.Application.TestRuns.SkipMissedScheduledTestRuns;

public sealed class SkipMissedScheduledTestRunsHandler(ITestRunRepository runs, TimeProvider timeProvider) : IRequestHandler<SkipMissedScheduledTestRuns, int>
{
    public async ValueTask<int> Handle(SkipMissedScheduledTestRuns request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        return await runs.SkipQueuedScheduledAsync(now, now, "Skipped: the app wasn't running at the scheduled time.", cancellationToken);
    }
}
