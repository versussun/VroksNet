using Mediator;
using VroksNet.Application.Abstractions;
using VroksNet.Application.TestScenarios;
using VroksNet.Domain.TestScenarios;

namespace VroksNet.Application.TestScenarios.CreateTestScenario;

public sealed class CreateTestScenarioHandler(
    ITestScenarioRepository repository,
    IApiSpecificationRepository specifications,
    IConnectionRepository connections) : IRequestHandler<CreateTestScenario, Guid>
{
    public async ValueTask<Guid> Handle(CreateTestScenario request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);

        var target = await TestScenarioTargetResolver.ResolveAsync(
            specifications, connections, request.SpecificationId, request.MockEndpointId, request.ConnectionId, cancellationToken);
        var (listenTimeoutSeconds, exchange) = TestScenarioTargetResolver.ValidateKindSettings(
            target, request.Kind, request.ListenTimeoutSeconds, request.Exchange);

        var scenario = new TestScenario
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            SpecificationId = request.SpecificationId,
            MockEndpointId = request.MockEndpointId,
            ConnectionId = request.ConnectionId,
            PayloadOverride = request.PayloadOverride,
            Kind = request.Kind,
            ListenTimeoutSeconds = listenTimeoutSeconds,
            Exchange = exchange,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await repository.InsertAsync(scenario, cancellationToken);

        return scenario.Id;
    }
}
