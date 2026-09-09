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
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);

        var scenario = await repository.FindByIdAsync(request.Id, cancellationToken);
        if (scenario is null)
        {
            return false;
        }

        await TestScenarioTargetResolver.ResolveAsync(
            specifications, connections, request.SpecificationId, request.MockEndpointId, request.ConnectionId, cancellationToken);

        scenario.Name = request.Name;
        scenario.SpecificationId = request.SpecificationId;
        scenario.MockEndpointId = request.MockEndpointId;
        scenario.ConnectionId = request.ConnectionId;
        scenario.PayloadOverride = request.PayloadOverride;
        scenario.UpdatedAt = DateTimeOffset.UtcNow;

        return await repository.UpdateAsync(scenario, cancellationToken);
    }
}
