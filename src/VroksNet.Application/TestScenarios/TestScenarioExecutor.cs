using System.Text.Json;
using VroksNet.Application.Abstractions;
using VroksNet.Application.CallRecords;
using VroksNet.Application.TestScenarios.RunTestScenario;
using VroksNet.Domain.CallRecords;
using VroksNet.Domain.Connections;
using VroksNet.Domain.MockEndpoints;
using VroksNet.Domain.TestRuns;
using VroksNet.Domain.TestScenarios;

namespace VroksNet.Application.TestScenarios;

/// <summary>
/// Runs a <see cref="TestScenario"/> once: sends its message (or, for a
/// <see cref="TestScenarioKind.Listen"/> scenario, waits for one on the operation's broker channel),
/// checks the response or message against the spec, logs a <see cref="CallRecord"/> tagged with
/// the run, and records the scenario's last-run fields. Shared by the synchronous
/// <c>RunTestScenario</c> and the background <c>ExecuteTestRun</c>, which own the
/// <see cref="TestRun"/>'s lifecycle around it.
/// <para>
/// A dangling specification, operation or connection is an expected runtime condition — one of
/// them may have been deleted since the scenario was saved — so it's reported as an unsuccessful
/// result rather than thrown. An HTTP response is checked against the operation's declared
/// responses (docs/contract-testing-plan.md, "Phase B"); a response that doesn't match fails the
/// run even though the send itself went through. A Listen run validates the received message
/// ("Phase C").
/// </para>
/// </summary>
public sealed class TestScenarioExecutor(
    ITestScenarioRepository scenarios,
    IApiSpecificationRepository specifications,
    IConnectionRepository connections,
    IMessageSender sender,
    IMessageListener listener,
    ISchemaValidator schemaValidator,
    IResponseTemplateEngine templateEngine,
    ICallRecordRepository callRecords)
{
    /// <summary>A Send has no incoming request: only <c>{{uuid}}</c>/<c>{{now}}</c> resolve, <c>{{request.*}}</c> become warnings — as for a Publisher.</summary>
    private static readonly TemplateContext NoRequest = new(
        new Dictionary<string, string>(), new Dictionary<string, string>(), new Dictionary<string, string>(), null);

    /// <param name="onListening">For a Listen scenario: called once it's subscribed (see <see cref="IMessageListener.ListenAsync"/>).</param>
    public async Task<RunTestScenarioResult> ExecuteAsync(TestScenario scenario, Guid testRunId, CancellationToken cancellationToken, Action? onListening = null)
    {
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
            ? await ListenAsync(scenario, endpoint, connection, onListening, cancellationToken)
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
            TestRunId = testRunId,
            Direction = outcome.Direction,
            Timestamp = ranAt,
            RequestSnapshot = CallRecordSnapshot.Truncate(outcome.RequestSnapshot),
            ResponseSnapshot = CallRecordSnapshot.Truncate(outcome.ResponseSnapshot),
            StatusCode = outcome.StatusCode,
            ContractValid = validation?.IsValid,
            ValidationErrors = validation is { IsValid: false } ? JsonSerializer.Serialize(validation.Errors) : null,
            Warnings = outcome.Warnings is { Count: > 0 } warnings ? JsonSerializer.Serialize(warnings) : null
        }, cancellationToken);

        await scenarios.RecordRunAsync(scenario.Id, ranAt, success, message, cancellationToken);

        return new RunTestScenarioResult(success, message, outcome.ResponseBody, outcome.StatusCode, validation);
    }

    /// <summary>The <see cref="TestRunOutcome"/> to record for a run that produced <paramref name="result"/>.</summary>
    public static TestRunOutcome OutcomeOf(RunTestScenarioResult result, DateTimeOffset finishedAt) => new(
        result.Success ? TestRunStatus.Passed : TestRunStatus.Failed,
        finishedAt,
        result.Message,
        result.StatusCode,
        result.ContractValidation?.IsValid,
        result.ContractValidation is { IsValid: false } validation ? JsonSerializer.Serialize(validation.Errors) : null);

    private async Task<RunOutcome> SendAsync(TestScenario scenario, MockEndpoint endpoint, Connection connection, CancellationToken cancellationToken)
    {
        // Filled in like a Publisher's payload, so a {{uuid}}/{{now}} in the example — the spec's own,
        // or one built from its schema — goes out as a value, not as the placeholder.
        var template = scenario.PayloadOverride ?? endpoint.ExampleTemplate;
        var rendered = template is null ? null : templateEngine.Render(template, NoRequest);
        var payload = rendered?.Text;
        var result = await sender.SendAsync(connection, endpoint.OperationKey, payload, scenario.BrokerOptions, cancellationToken);

        var isHttp = ServiceTypeTraits.Find(connection.ServiceType)?.IsHttp == true;
        var validation = result.Success && isHttp
            ? ValidateResponse(endpoint, result)
            : null;

        return new RunOutcome(
            result.Success,
            result.Message,
            result.ResponseBody,
            result.StatusCode,
            validation,
            isHttp ? CallDirection.OutboundHttpRequest : CallDirection.OutboundBrokerPublish,
            RequestSnapshot: payload,
            ResponseSnapshot: result.Success ? result.ResponseBody ?? result.Message : result.Message,
            ViolationSubject: "response",
            Warnings: rendered?.Warnings);
    }

    /// <summary>
    /// Waits for the next message on the operation's channel and validates it against the
    /// operation's payload schema (<see cref="MockEndpoint.ResponseSchema"/> for AsyncAPI) — no
    /// schema means nothing to check. Not receiving anything within the timeout fails the run.
    /// </summary>
    private async Task<RunOutcome> ListenAsync(TestScenario scenario, MockEndpoint endpoint, Connection connection, Action? onListening, CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromSeconds(scenario.ListenTimeoutSeconds ?? TestScenarioListening.DefaultTimeoutSeconds);
        var result = await listener.ListenAsync(connection, endpoint.OperationKey, timeout, scenario.BrokerOptions, cancellationToken, onListening);

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
        string ViolationSubject,
        IReadOnlyList<string>? Warnings = null);
}
