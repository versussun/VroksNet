using Mediator;
using VroksNet.Application.Abstractions;
using VroksNet.Domain.TestRuns;
using VroksNet.Domain.TestSuites;

namespace VroksNet.Application.TestSuites.StartStartupSuiteRuns;

public sealed class StartStartupSuiteRunsHandler(
    ITestSuiteRepository suites,
    ISuiteRunRepository suiteRuns,
    TimeProvider timeProvider) : IRequestHandler<StartStartupSuiteRuns, int>
{
    public async ValueTask<int> Handle(StartStartupSuiteRuns request, CancellationToken cancellationToken)
    {
        var started = 0;
        foreach (var suite in (await suites.ListAsync(cancellationToken)).Where(suite => suite.RunOnStartup))
        {
            await suiteRuns.InsertAsync(new SuiteRun
            {
                Id = Guid.NewGuid(),
                TestSuiteId = suite.Id,
                Status = TestRunStatus.Queued,
                Trigger = TestRunTrigger.Startup,
                ScheduledFor = timeProvider.GetUtcNow()
            }, cancellationToken);
            started++;
        }

        return started;
    }
}
