using System.Text.Json;
using Mediator;
using VroksNet.Application.Abstractions;
using VroksNet.Application.CallRecords;
using VroksNet.Domain.CallRecords;
using VroksNet.Domain.Connections;
using VroksNet.Domain.MockEndpoints;
using VroksNet.Domain.TestScenarios;

namespace VroksNet.Application.TestScenarios.RunTestScenario;

/// <summary>
/// Sends a saved <see cref="Domain.TestScenarios.TestScenario"/>'s message and logs a
/// <see cref="CallRecord"/> either way. Unlike create/update (which validate new input and throw
/// on a bad reference), a dangling specification/operation/connection here is an expected runtime
/// condition — one of them may have been deleted since the scenario was saved — so it's reported
/// back as an unsuccessful result rather than thrown. An HTTP response is also checked against the
/// operation's declared responses (see docs/contract-testing-plan.md, "Фаза B"); a response that
/// doesn't match fails the run even though the send itself went through. A
/// <see cref="TestScenarioKind.Listen"/> scenario instead waits for a message on the operation's
/// broker channel and validates that ("Фаза C").
/// </summary>
public sealed class RunTestScenarioHandler(
    ITestScenarioRepository scenarios,
    IApiSpecificationRepository specifications,
    IConnectionRepository connections,
    IMessageSender sender,
    IMessageListener listener,
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

        var outcome = scenario.Kind == TestScenarioKind.Listen
            ? await ListenAsync(scenario, endpoint, connection, cancellationToken)
            : await SendAsync(scenario, endpoint, connection, cancellationToken);
        var ranAt = DateTimeOffset.UtcNow;

        var validation = outcome.Validation;
        var success = outcome.Success && validation?.IsValid != false;
        var message = validation?.IsValid == false
            ? $"{outcome.Message} — {outcome.ViolationSubject} doesn't match the spec ({validation.Errors.Count} violation(s))."
            : outcome.Message;

        await callRecords.InsertAsync(new CallRecord
        {
            Id = Guid.NewGuid(),
            SpecificationId = scenario.SpecificationId,
            MockEndpointId = scenario.MockEndpointId,
            ConnectionId = connection.Id,
            TestScenarioId = scenario.Id,
            Direction = outcome.Direction,
            Timestamp = ranAt,
            RequestSnapshot = CallRecordSnapshot.Truncate(outcome.RequestSnapshot),
            ResponseSnapshot = CallRecordSnapshot.Truncate(outcome.ResponseSnapshot),
            StatusCode = outcome.StatusCode,
            ContractValid = validation?.IsValid,
            ValidationErrors = validation is { IsValid: false } ? JsonSerializer.Serialize(validation.Errors) : null
        }, cancellationToken);

        await scenarios.RecordRunAsync(scenario.Id, ranAt, success, message, cancellationToken);

        return new RunTestScenarioResult(success, message, outcome.ResponseBody, outcome.StatusCode, validation);
    }

    private async Task<RunOutcome> SendAsync(TestScenario scenario, MockEndpoint endpoint, Connection connection, CancellationToken cancellationToken)
    {
        var payload = scenario.PayloadOverride ?? endpoint.ExampleTemplate;
        var result = await sender.SendAsync(connection, endpoint.OperationKey, payload, scenario.Exchange, cancellationToken);

        var validation = result.Success && connection.ServiceType == ConnectionServiceType.Http
            ? ValidateResponse(endpoint, result)
            : null;

        return new RunOutcome(
            result.Success,
            result.Message,
            result.ResponseBody,
            result.StatusCode,
            validation,
            connection.ServiceType == ConnectionServiceType.Http ? CallDirection.OutboundHttpRequest : CallDirection.OutboundBrokerPublish,
            RequestSnapshot: payload,
            ResponseSnapshot: result.Success ? result.ResponseBody ?? result.Message : result.Message,
            ViolationSubject: "response");
    }

    /// <summary>
    /// Waits for the next message on the operation's channel and validates it against the
    /// operation's payload schema (<see cref="MockEndpoint.ResponseSchema"/> for AsyncAPI) — no
    /// schema means nothing to check. Not receiving anything within the timeout fails the run.
    /// </summary>
    private async Task<RunOutcome> ListenAsync(TestScenario scenario, MockEndpoint endpoint, Connection connection, CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromSeconds(scenario.ListenTimeoutSeconds ?? TestScenarioListening.DefaultTimeoutSeconds);
        var exchange = scenario.Exchange ?? TestScenarioListening.DefaultRabbitMqExchange;
        var result = await listener.ListenAsync(connection, endpoint.OperationKey, timeout, exchange, cancellationToken);

        SchemaValidationResult? validation = null;
        if (result.Received && endpoint.ResponseSchema is { } schema)
        {
            validation = string.IsNullOrWhiteSpace(result.Payload)
                ? new SchemaValidationResult(false, ["The message is empty, but the spec declares a payload schema."])
                : schemaValidator.Validate(schema, result.Payload);
        }

        return new RunOutcome(
            result.Received,
            result.Message,
            result.Payload,
            StatusCode: null,
            validation,
            CallDirection.InboundBrokerMessage,
            RequestSnapshot: null,
            ResponseSnapshot: result.Received ? result.Payload : result.Message,
            ViolationSubject: "message");
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

    /// <param name="ViolationSubject">What a contract violation is about, for the run message: "response" or "message".</param>
    private sealed record RunOutcome(
        bool Success,
        string Message,
        string? ResponseBody,
        int? StatusCode,
        SchemaValidationResult? Validation,
        CallDirection Direction,
        string? RequestSnapshot,
        string? ResponseSnapshot,
        string ViolationSubject);
}
