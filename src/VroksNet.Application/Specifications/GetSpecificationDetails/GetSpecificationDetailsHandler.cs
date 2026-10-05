using Mediator;
using VroksNet.Application.Abstractions;
using VroksNet.Domain.Connections;
using VroksNet.Domain.TestScenarios;

namespace VroksNet.Application.Specifications.GetSpecificationDetails;

public sealed class GetSpecificationDetailsHandler(IApiSpecificationRepository repository)
    : IRequestHandler<GetSpecificationDetails, SpecificationDetails?>
{
    public async ValueTask<SpecificationDetails?> Handle(GetSpecificationDetails request, CancellationToken cancellationToken)
    {
        var specification = await repository.FindByIdAsync(request.Id, cancellationToken);
        if (specification is null)
        {
            return null;
        }

        return new SpecificationDetails(
            specification.Id,
            specification.Title,
            specification.Kind,
            specification.RawContent,
            specification.CreatedAt,
            specification.UpdatedAt,
            specification.Endpoints
                .OrderBy(endpoint => endpoint.OperationKey, StringComparer.Ordinal)
                .Select(endpoint => new MockEndpointDetail(
                    endpoint.Id,
                    endpoint.OperationKey,
                    endpoint.IsEnabled,
                    endpoint.ServeAtRealPath,
                    endpoint.ExampleTemplate,
                    endpoint.ExampleIsGenerated,
                    OperationCompatibility.IsHttpOperation(endpoint.OperationKey),
                    OperationCompatibility.ShapeOf(endpoint.OperationKey),
                    TestScenarioListening.CanListen(endpoint.OperationKey),
                    TestScenarioListening.DefaultKindFor(endpoint.OperationKey)))
                .ToList(),
            specification.Protocols,
            ServiceTypeTraits.TypesFor(specification.Protocols));
    }
}
