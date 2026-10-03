using Mediator;
using VroksNet.Application.Abstractions;
using VroksNet.Domain.TestRuns;

namespace VroksNet.Application.TestRuns.CancelTestRun;

public sealed class CancelTestRunHandler(
    ITestRunRepository runs,
    TestRunCancellations cancellations,
    TimeProvider timeProvider) : IRequestHandler<CancelTestRun, CancelTestRunResult>
{
    public async ValueTask<CancelTestRunResult> Handle(CancelTestRun request, CancellationToken cancellationToken)
    {
        var run = await runs.FindByIdAsync(request.RunId, cancellationToken);
        if (run is null)
        {
            return CancelTestRunResult.NotFound;
        }

        if (run.Status == TestRunStatus.Queued && await runs.CancelQueuedAsync(run.Id, timeProvider.GetUtcNow(), cancellationToken))
        {
            return CancelTestRunResult.Cancelled;
        }

        // Running — or it just started, between the read and the conditional cancel above.
        return cancellations.Cancel(run.Id) ? CancelTestRunResult.Cancelled : CancelTestRunResult.NotActive;
    }
}
