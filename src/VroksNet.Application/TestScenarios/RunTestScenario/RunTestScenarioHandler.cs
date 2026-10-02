using System.Text.Json;
using Mediator;
using VroksNet.Application.Abstractions;
using VroksNet.Application.CallRecords;
using VroksNet.Domain.CallRecords;
using VroksNet.Domain.Connections;
using VroksNet.Domain.MockEndpoints;

namespace VroksNet.Application.TestScenarios.RunTestScenario;

/// <summary>
/// Sends a saved <see cref="Domain.TestScenarios.TestScenario"/>'s message and logs a
/// <see cref="CallRecord"/> either way. Unlike create/update (which validate new input and throw
/// on a bad reference), a dangling specification/operation/connection here is an expected runtime
/// condition — one of them may have been deleted since the scenario was saved — so it's reported
/// back as an unsuccessful result rather than thrown. An HTTP response is also checked against the
/// operation's declared responses (see docs/contract-testing-plan.md, "Фаза B"); a response that
/// doesn't match fails the run even though the send itself went through.
/// </summary>
public sealed class RunTestScenarioHandler(
    ITestScenarioRepository scenarios,
    IApiSpecificationRepository specifications,
    IConnectionRepository connections,
    IMessageSender sender,
    ISchemaValidator schemaValidator,
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
            const string missingMessage = "The scenario's specification, operation, or connection no longer exists.";
            await scenarios.RecordRunAsync(scenario.Id, DateTimeOffset.UtcNow, success: false, missingMessage, cancellationToken);
            return new RunTestScenarioResult(false, missingMessage, null);
        }

        var payload = scenario.PayloadOverride ?? endpoint.ExampleTemplate;
        var result = await sender.SendAsync(connection, endpoint.OperationKey, payload, cancellationToken);
        var ranAt = DateTimeOffset.UtcNow;

        var validation = result.Success && connection.ServiceType == ConnectionServiceType.Http
            ? ValidateResponse(endpoint, result)
            : null;
        var success = result.Success && validation?.IsValid != false;
        var message = validation?.IsValid == false
            ? $"{result.Message} — response doesn't match the spec ({validation.Errors.Count} violation(s))."
            : result.Message;

        await callRecords.InsertAsync(new CallRecord
        {
            Id = Guid.NewGuid(),
            SpecificationId = scenario.SpecificationId,
            MockEndpointId = scenario.MockEndpointId,
            ConnectionId = connection.Id,
            TestScenarioId = scenario.Id,
            Direction = connection.ServiceType == ConnectionServiceType.Http ? CallDirection.OutboundHttpRequest : CallDirection.OutboundBrokerPublish,
            Timestamp = ranAt,
            RequestSnapshot = CallRecordSnapshot.Truncate(payload),
            ResponseSnapshot = CallRecordSnapshot.Truncate(result.Success ? result.ResponseBody ?? result.Message : result.Message),
            StatusCode = result.StatusCode,
            ContractValid = validation?.IsValid,
            ValidationErrors = validation is { IsValid: false } ? JsonSerializer.Serialize(validation.Errors) : null
        }, cancellationToken);

        await scenarios.RecordRunAsync(scenario.Id, ranAt, success, message, cancellationToken);

        return new RunTestScenarioResult(success, message, result.ResponseBody, result.StatusCode, validation);
    }

    /// <summary>
    /// Null when there's nothing to validate against: no status code, or an operation with no
    /// declared responses (an AsyncAPI one, or an OpenAPI one imported before they were stored —
    /// re-importing the spec fixes that).
    /// </summary>
    private SchemaValidationResult? ValidateResponse(MockEndpoint endpoint, MessageSendResult result)
    {
        if (result.StatusCode is not { } statusCode || endpoint.ResponseSchemasByStatus.Count == 0)
        {
            return null;
        }

        if (!endpoint.TryGetDeclaredResponse(statusCode, out var schemaJson))
        {
            return new SchemaValidationResult(false, [$"Status {statusCode} isn't declared for {endpoint.OperationKey} in the spec."]);
        }

        if (schemaJson is null)
        {
            // The spec declares this status without a JSON body — nothing more to check.
            return SchemaValidationResult.Valid;
        }

        if (string.IsNullOrWhiteSpace(result.ResponseBody))
        {
            return new SchemaValidationResult(false, [$"The response body is empty, but the spec declares a JSON body for status {statusCode}."]);
        }

        return schemaValidator.Validate(schemaJson, result.ResponseBody);
    }
}
