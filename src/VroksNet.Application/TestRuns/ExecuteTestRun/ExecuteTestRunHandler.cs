using Mediator;
using VroksNet.Application.Abstractions;

namespace VroksNet.Application.TestRuns.ExecuteTestRun;

/// <summary>Runs one queued run through <see cref="TestRunRunner"/>, which owns its lifecycle.</summary>
public sealed class ExecuteTestRunHandler(ITestRunRepository runs, TestRunRunner runner) : IRequestHandler<ExecuteTestRun>
{
    public async ValueTask<Unit> Handle(ExecuteTestRun request, CancellationToken cancellationToken)
    {
        if (await runs.FindByIdAsync(request.RunId, cancellationToken) is { } run)
        {
            await runner.RunAsync(run, cancellationToken);
        }

        return Unit.Value;
    }
}
