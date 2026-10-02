using VroksNet.Domain.ApiSpecifications;
using VroksNet.Domain.Connections;
using VroksNet.Domain.TestScenarios;

namespace VroksNet.Application.TestScenarios.ListTestScenarios;

/// <summary>Builds a <see cref="TestScenarioSummary"/> from a <see cref="TestScenario"/> plus its already-resolved specification/connection — shared by <c>ListTestScenariosHandler</c> and <c>GetTestScenarioHandler</c> so both denormalize the same way.</summary>
internal static class TestScenarioSummaryFactory
{
    public static TestScenarioSummary Build(TestScenario scenario, ApiSpecification? specification, Connection? connection)
    {
        var endpoint = specification?.Endpoints.FirstOrDefault(e => e.Id == scenario.MockEndpointId);

        return new TestScenarioSummary(
            scenario.Id,
            scenario.Name,
            scenario.SpecificationId,
            specification?.Title ?? "(deleted specification)",
            scenario.MockEndpointId,
            endpoint?.OperationKey ?? "(deleted operation)",
            scenario.ConnectionId,
            connection?.Name ?? "(deleted connection)",
            connection?.ServiceType ?? default,
            scenario.PayloadOverride,
            scenario.Kind,
            scenario.ListenTimeoutSeconds,
            scenario.Exchange,
            scenario.UpdatedAt,
            scenario.LastRunAt,
            scenario.LastRunSuccess,
            scenario.LastRunMessage);
    }
}
