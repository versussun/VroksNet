using Mediator;
using VroksNet.Application.Abstractions;
using VroksNet.Application.TestScenarios;
using VroksNet.Domain.TestScenarios;

namespace VroksNet.Application.TestScenarios.CreateTestScenario;

public sealed class CreateTestScenarioHandler(
    ITestScenarioRepository repository,
    IApiSpecificationRepository specifications,
    IConnectionRepository connections,
    ICronSchedule cron) : IRequestHandler<CreateTestScenario, Guid>
{
    public async ValueTask<Guid> Handle(CreateTestScenario request, CancellationToken cancellationToken)
    {
        var name = UniqueNames.Normalize(request.Name, "test scenario");
        UniqueNames.EnsureFree((await repository.FindByNameAsync(name, cancellationToken))?.Id, null, "test scenario", name);

        var target = await TestScenarioTargetResolver.ResolveAsync(
            specifications, connections, request.SpecificationId, request.MockEndpointId, request.ConnectionId, cancellationToken);
        var (listenTimeoutSeconds, exchange) = TestScenarioTargetResolver.ValidateKindSettings(
            target, request.Kind, request.ListenTimeoutSeconds, request.Exchange);
        var (schedule, scheduleTimeZone) = TestScenarioSchedules.Normalize(cron, request.Schedule, request.ScheduleTimeZone);

        var scenario = new TestScenario
        {
            Id = Guid.NewGuid(),
            Name = name,
            SpecificationId = request.SpecificationId,
            MockEndpointId = request.MockEndpointId,
            ConnectionId = request.ConnectionId,
            PayloadOverride = request.PayloadOverride,
            Kind = request.Kind,
            ListenTimeoutSeconds = listenTimeoutSeconds,
            Exchange = exchange,
            Schedule = schedule,
            ScheduleTimeZone = scheduleTimeZone,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await repository.InsertAsync(scenario, cancellationToken);

        return scenario.Id;
    }
}
