using Mediator;
using VroksNet.Application.Abstractions;

namespace VroksNet.Application.TestRuns.ListDueTestRuns;

public sealed class ListDueTestRunsHandler(ITestRunRepository runs) : IRequestHandler<ListDueTestRuns, IReadOnlyList<DueTestRun>>
{
    public async ValueTask<IReadOnlyList<DueTestRun>> Handle(ListDueTestRuns request, CancellationToken cancellationToken)
    {
        var due = await runs.ListDueAsync(request.Now, cancellationToken);
        return due.Select(run => new DueTestRun(run.Id, run.TestScenarioId)).ToList();
    }
}
