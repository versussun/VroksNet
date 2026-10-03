using Mediator;
using VroksNet.Application.Abstractions;
using VroksNet.Application.TestScenarios;

namespace VroksNet.Application.TestScenarios.UpdateTestScenario;

public sealed class UpdateTestScenarioHandler(
    ITestScenarioRepository repository,
    IApiSpecificationRepository specifications,
    IConnectionRepository connections,
    ITestRunRepository runs,
    ICronSchedule cron,
    IBrokerRules brokerRules) : IRequestHandler<UpdateTestScenario, bool>
{
    public async ValueTask<bool> Handle(UpdateTestScenario request, CancellationToken cancellationToken)
    {
        var name = UniqueNames.Normalize(request.Name, "test scenario");

        var scenario = await repository.FindByIdAsync(request.Id, cancellationToken);
        if (scenario is null)
        {
            return false;
        }

        UniqueNames.EnsureFree((await repository.FindByNameAsync(name, cancellationToken))?.Id, scenario.Id, "test scenario", name);

        var target = await TestScenarioTargetResolver.ResolveAsync(
            specifications, connections, request.SpecificationId, request.MockEndpointId, request.ConnectionId, cancellationToken);
        var (listenTimeoutSeconds, brokerOptions) = TestScenarioTargetResolver.ValidateKindSettings(
            brokerRules, target, request.Kind, request.ListenTimeoutSeconds, request.Exchange, request.BrokerOptions);
        var (schedule, scheduleTimeZone) = TestScenarioSchedules.Normalize(cron, request.Schedule, request.ScheduleTimeZone);
        var scheduleChanged = schedule != scenario.Schedule || scheduleTimeZone != scenario.ScheduleTimeZone;

        scenario.Name = name;
        scenario.SpecificationId = request.SpecificationId;
        scenario.MockEndpointId = request.MockEndpointId;
        scenario.ConnectionId = request.ConnectionId;
        scenario.PayloadOverride = request.PayloadOverride;
        scenario.Kind = request.Kind;
        scenario.ListenTimeoutSeconds = listenTimeoutSeconds;
        scenario.BrokerOptions = brokerOptions;
        scenario.Schedule = schedule;
        scenario.ScheduleTimeZone = scheduleTimeZone;
        scenario.UpdatedAt = DateTimeOffset.UtcNow;

        if (!await repository.UpdateAsync(scenario, cancellationToken))
        {
            return false;
        }

        // The queued run was planned on the old schedule; the worker plans the next one on the new.
        if (scheduleChanged)
        {
            await runs.DeleteQueuedScheduledAsync(scenario.Id, cancellationToken);
        }

        return true;
    }
}
