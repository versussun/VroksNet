using Mediator;
using VroksNet.Application.Abstractions;

namespace VroksNet.Application.TestRuns.PruneTestRunHistory;

/// <summary>Suite runs are kept to the same number per suite.</summary>
public sealed class PruneTestRunHistoryHandler(ITestRunRepository runs, ISuiteRunRepository suiteRuns) : IRequestHandler<PruneTestRunHistory, int>
{
    public async ValueTask<int> Handle(PruneTestRunHistory request, CancellationToken cancellationToken)
    {
        var keep = Math.Max(1, request.KeepPerScenario);
        return await runs.PruneAsync(keep, cancellationToken) + await suiteRuns.PruneAsync(keep, cancellationToken);
    }
}
