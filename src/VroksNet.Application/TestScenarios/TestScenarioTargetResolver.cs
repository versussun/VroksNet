using VroksNet.Application.Abstractions;
using VroksNet.Domain.TestScenarios;

namespace VroksNet.Application.TestScenarios;

/// <summary>Shared by <c>CreateTestScenarioHandler</c>/<c>UpdateTestScenarioHandler</c> so both validate the same way.</summary>
public static class TestScenarioTargetResolver
{
    /// <summary>Throws <see cref="ArgumentException"/> if the specification/operation/connection don't exist, or the operation can't be sent through that connection's service type — see <see cref="OperationCompatibility"/>.</summary>
    public static async Task<ResolvedTestScenarioTarget> ResolveAsync(
        IApiSpecificationRepository specifications,
        IConnectionRepository connections,
        Guid specificationId,
        Guid mockEndpointId,
        Guid connectionId,
        CancellationToken cancellationToken)
    {
        var specification = await specifications.FindByIdAsync(specificationId, cancellationToken)
            ?? throw new ArgumentException($"No specification with id '{specificationId}' exists.");

        var endpoint = specification.Endpoints.FirstOrDefault(e => e.Id == mockEndpointId)
            ?? throw new ArgumentException($"No operation with id '{mockEndpointId}' exists in specification '{specification.Title}'.");

        var connection = await connections.FindByIdAsync(connectionId, cancellationToken)
            ?? throw new ArgumentException($"No connection with id '{connectionId}' exists.");

        if (!OperationCompatibility.IsCompatible(endpoint.OperationKey, connection.ServiceType))
        {
            throw new ArgumentException($"Operation \"{endpoint.OperationKey}\" can't be sent through a {connection.ServiceType} connection.");
        }

        return new ResolvedTestScenarioTarget(specification, endpoint, connection);
    }
}
