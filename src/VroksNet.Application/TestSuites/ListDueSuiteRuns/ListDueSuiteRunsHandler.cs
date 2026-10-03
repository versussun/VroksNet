using Mediator;
using VroksNet.Application.Abstractions;

namespace VroksNet.Application.TestSuites.ListDueSuiteRuns;

public sealed class ListDueSuiteRunsHandler(ISuiteRunRepository suiteRuns) : IRequestHandler<ListDueSuiteRuns, IReadOnlyList<DueSuiteRun>>
{
    public async ValueTask<IReadOnlyList<DueSuiteRun>> Handle(ListDueSuiteRuns request, CancellationToken cancellationToken)
        => (await suiteRuns.ListDueAsync(request.Now, cancellationToken)).Select(run => new DueSuiteRun(run.Id, run.TestSuiteId)).ToList();
}
