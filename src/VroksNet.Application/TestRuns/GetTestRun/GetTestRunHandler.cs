using Mediator;
using VroksNet.Application.Abstractions;

namespace VroksNet.Application.TestRuns.GetTestRun;

public sealed class GetTestRunHandler(ITestRunRepository runs) : IRequestHandler<GetTestRun, TestRunSummary?>
{
    public async ValueTask<TestRunSummary?> Handle(GetTestRun request, CancellationToken cancellationToken)
        => await runs.FindByIdAsync(request.Id, cancellationToken) is { } run ? TestRunSummaries.From(run) : null;
}
