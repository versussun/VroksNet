using Mediator;
using VroksNet.Application.Abstractions;

namespace VroksNet.Application.TestRuns.PruneTestRunHistory;

public sealed class PruneTestRunHistoryHandler(ITestRunRepository runs) : IRequestHandler<PruneTestRunHistory, int>
{
    public async ValueTask<int> Handle(PruneTestRunHistory request, CancellationToken cancellationToken)
        => await runs.PruneAsync(Math.Max(1, request.KeepPerScenario), cancellationToken);
}
