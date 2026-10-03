using Mediator;
using VroksNet.Application.Abstractions;
using VroksNet.Application.TestRuns;
using VroksNet.Application.TestRuns.CancelTestRun;
using VroksNet.Domain.TestRuns;

namespace VroksNet.Application.TestSuites.CancelSuiteRun;

public sealed class CancelSuiteRunHandler(
    ISuiteRunRepository suiteRuns,
    TestRunCancellations cancellations,
    TimeProvider timeProvider) : IRequestHandler<CancelSuiteRun, CancelTestRunResult>
{
    public async ValueTask<CancelTestRunResult> Handle(CancelSuiteRun request, CancellationToken cancellationToken)
    {
        var run = await suiteRuns.FindByIdAsync(request.Id, cancellationToken);
        if (run is null)
        {
            return CancelTestRunResult.NotFound;
        }

        if (run.Status == TestRunStatus.Queued && await suiteRuns.CancelQueuedAsync(run.Id, timeProvider.GetUtcNow(), cancellationToken))
        {
            return CancelTestRunResult.Cancelled;
        }

        // Its runs are linked to its token, so they stop with it.
        return cancellations.Cancel(run.Id) ? CancelTestRunResult.Cancelled : CancelTestRunResult.NotActive;
    }
}
