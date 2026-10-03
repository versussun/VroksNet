using Mediator;
using VroksNet.Application.Abstractions;

namespace VroksNet.Application.TestRuns.InterruptRunningTestRuns;

public sealed class InterruptRunningTestRunsHandler(ITestRunRepository runs, TimeProvider timeProvider) : IRequestHandler<InterruptRunningTestRuns, int>
{
    public async ValueTask<int> Handle(InterruptRunningTestRuns request, CancellationToken cancellationToken)
        => await runs.InterruptRunningAsync(timeProvider.GetUtcNow(), "Interrupted: the app restarted while it ran.", cancellationToken);
}
