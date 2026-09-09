using Mediator;
using VroksNet.Application.Abstractions;
using VroksNet.Domain.CallRecords;
using VroksNet.Domain.Connections;

namespace VroksNet.Application.TestScenarios.RunTestScenario;

/// <summary>
/// Sends a saved <see cref="Domain.TestScenarios.TestScenario"/>'s message and logs a
/// <see cref="CallRecord"/> either way. Unlike create/update (which validate new input and throw
/// on a bad reference), a dangling specification/operation/connection here is an expected runtime
/// condition — one of them may have been deleted since the scenario was saved — so it's reported
/// back as an unsuccessful result rather than thrown.
/// </summary>
public sealed class RunTestScenarioHandler(
    ITestScenarioRepository scenarios,
    IApiSpecificationRepository specifications,
    IConnectionRepository connections,
    IMessageSender sender,
    ICallRecordRepository callRecords) : IRequestHandler<RunTestScenario, RunTestScenarioResult?>
{
    public async ValueTask<RunTestScenarioResult?> Handle(RunTestScenario request, CancellationToken cancellationToken)
    {
        var scenario = await scenarios.FindByIdAsync(request.Id, cancellationToken);
        if (scenario is null)
        {
            return null;
        }

        var specification = await specifications.FindByIdAsync(scenario.SpecificationId, cancellationToken);
        var endpoint = specification?.Endpoints.FirstOrDefault(e => e.Id == scenario.MockEndpointId);
        var connection = await connections.FindByIdAsync(scenario.ConnectionId, cancellationToken);

        if (endpoint is null || connection is null)
        {
            return new RunTestScenarioResult(false, "The scenario's specification, operation, or connection no longer exists.", null);
        }

        var payload = scenario.PayloadOverride ?? endpoint.ExampleTemplate;
        var result = await sender.SendAsync(connection, endpoint.OperationKey, payload, cancellationToken);

        await callRecords.InsertAsync(new CallRecord
        {
            Id = Guid.NewGuid(),
            SpecificationId = scenario.SpecificationId,
            MockEndpointId = scenario.MockEndpointId,
            ConnectionId = connection.Id,
            Direction = connection.ServiceType == ConnectionServiceType.Http ? CallDirection.OutboundHttpRequest : CallDirection.OutboundBrokerPublish,
            Timestamp = DateTimeOffset.UtcNow,
            RequestSnapshot = payload,
            ResponseSnapshot = result.Success ? result.ResponseBody ?? result.Message : result.Message
        }, cancellationToken);

        return new RunTestScenarioResult(result.Success, result.Message, result.ResponseBody);
    }
}
