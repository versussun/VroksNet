using Mediator;
using VroksNet.Application.Abstractions;
using VroksNet.Application.TestScenarios;

namespace VroksNet.Application.TestScenarios.UpdateTestScenario;

public sealed class UpdateTestScenarioHandler(
    ITestScenarioRepository repository,
    IApiSpecificationRepository specifications,
    IConnectionRepository connections) : IRequestHandler<UpdateTestScenario, bool>
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
        var (listenTimeoutSeconds, exchange) = TestScenarioTargetResolver.ValidateKindSettings(
            target, request.Kind, request.ListenTimeoutSeconds, request.Exchange);

        scenario.Name = name;
        scenario.SpecificationId = request.SpecificationId;
        scenario.MockEndpointId = request.MockEndpointId;
        scenario.ConnectionId = request.ConnectionId;
        scenario.PayloadOverride = request.PayloadOverride;
        scenario.Kind = request.Kind;
        scenario.ListenTimeoutSeconds = listenTimeoutSeconds;
        scenario.Exchange = exchange;
        scenario.UpdatedAt = DateTimeOffset.UtcNow;

        return await repository.UpdateAsync(scenario, cancellationToken);
    }
}
