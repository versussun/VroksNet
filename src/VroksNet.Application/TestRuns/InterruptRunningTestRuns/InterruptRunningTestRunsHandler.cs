using Mediator;
using VroksNet.Application.Abstractions;

namespace VroksNet.Application.TestRuns.InterruptRunningTestRuns;

/// <summary>Suite runs too: their runs are among the ones interrupted.</summary>
public sealed class InterruptRunningTestRunsHandler(ITestRunRepository runs, ISuiteRunRepository suiteRuns, TimeProvider timeProvider) : IRequestHandler<InterruptRunningTestRuns, int>
{
    private const string Message = "Interrupted: the app restarted while it ran.";

    public async ValueTask<int> Handle(InterruptRunningTestRuns request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        return await runs.InterruptRunningAsync(now, Message, cancellationToken)
            + await suiteRuns.InterruptRunningAsync(now, Message, cancellationToken);
    }
}
