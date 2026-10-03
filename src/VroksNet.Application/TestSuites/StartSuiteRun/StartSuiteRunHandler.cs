using Mediator;
using VroksNet.Application.Abstractions;
using VroksNet.Domain.TestRuns;
using VroksNet.Domain.TestSuites;

namespace VroksNet.Application.TestSuites.StartSuiteRun;

public sealed class StartSuiteRunHandler(
    ITestSuiteRepository suites,
    ISuiteRunRepository suiteRuns,
    TimeProvider timeProvider) : IRequestHandler<StartSuiteRun, Guid?>
{
    public async ValueTask<Guid?> Handle(StartSuiteRun request, CancellationToken cancellationToken)
    {
        if (await TestSuiteKeys.FindAsync(suites, request.Key, cancellationToken) is not { } suite)
        {
            return null;
        }

        var run = new SuiteRun
        {
            Id = Guid.NewGuid(),
            TestSuiteId = suite.Id,
            Status = TestRunStatus.Queued,
            Trigger = TestRunTrigger.Manual,
            ScheduledFor = timeProvider.GetUtcNow()
        };
        await suiteRuns.InsertAsync(run, cancellationToken);
        return run.Id;
    }
}
