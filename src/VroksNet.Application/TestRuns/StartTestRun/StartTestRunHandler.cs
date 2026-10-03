using Mediator;
using VroksNet.Application.Abstractions;
using VroksNet.Domain.TestRuns;

namespace VroksNet.Application.TestRuns.StartTestRun;

public sealed class StartTestRunHandler(
    ITestScenarioRepository scenarios,
    ITestRunRepository runs,
    TimeProvider timeProvider) : IRequestHandler<StartTestRun, Guid?>
{
    public async ValueTask<Guid?> Handle(StartTestRun request, CancellationToken cancellationToken)
    {
        if (await scenarios.FindByIdAsync(request.TestScenarioId, cancellationToken) is null)
        {
            return null;
        }

        var run = new TestRun
        {
            Id = Guid.NewGuid(),
            TestScenarioId = request.TestScenarioId,
            Status = TestRunStatus.Queued,
            Trigger = TestRunTrigger.Manual,
            ScheduledFor = timeProvider.GetUtcNow()
        };
        await runs.InsertAsync(run, cancellationToken);
        return run.Id;
    }
}
