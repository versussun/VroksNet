using System.Text.Json;
using Mediator;
using Microsoft.Extensions.Logging;
using VroksNet.Application.Abstractions;
using VroksNet.Application.CallRecords;
using VroksNet.Domain.CallRecords;

namespace VroksNet.Application.Mocking.InvokeMockEndpoint;

/// <summary>
/// Answers a mock call from the matching enabled endpoint's example and logs it to the call
/// history (<see cref="CallDirection.InboundHttpRequest"/>) — unmatched calls too, since "why did
/// my service get a 404?" is exactly what the history is for. Logging is best-effort: a failed
/// history write is logged as a warning and the mock still answers. A request body is checked
/// against the operation's request schema ("Тип 3" in docs/contract-testing-plan.md) and the
/// outcome logged; the response doesn't change either way — the mock still answers the caller.
/// </summary>
public sealed class InvokeMockEndpointHandler(
    IApiSpecificationRepository repository,
    ICallRecordRepository callRecords,
    ISchemaValidator schemaValidator,
    ILogger<InvokeMockEndpointHandler> logger)
    : IRequestHandler<InvokeMockEndpoint, MockInvocationResult>
{
    /// <summary>Stands in for an operation the spec gave no example for, until schema-based generation exists.</summary>
    private const string EmptyExampleBody = "{}";

    public async ValueTask<MockInvocationResult> Handle(InvokeMockEndpoint request, CancellationToken cancellationToken)
    {
        var specifications = await repository.ListAsync(cancellationToken);

        var match = specifications
            .SelectMany(specification => specification.Endpoints)
            .Where(endpoint => endpoint.IsEnabled && (!request.ProviderMode || endpoint.ServeAtRealPath))
            .FirstOrDefault(endpoint => OperationKeyMatcher.Matches(endpoint.OperationKey, MatchMethod(request.Method), request.Path));

        var result = match is null
            ? new MockInvocationResult(false, null, null, request.ProviderMode
                ? $"No operation is served at {request.Method} {request.Path} — turn on \"Serve at real path\" for it."
                : $"No enabled mock endpoint matches {request.Method} {request.Path}.")
            : new MockInvocationResult(true, match.OperationKey, match.ExampleTemplate, match.ExampleTemplate ?? EmptyExampleBody);

        var validation = match?.RequestSchema is { } schema ? ValidateRequestBody(schema, request.Body, request.ContentType) : null;

        try
        {
            await callRecords.InsertAsync(new CallRecord
            {
                Id = Guid.NewGuid(),
                SpecificationId = match?.SpecificationId,
                MockEndpointId = match?.Id,
                Direction = CallDirection.InboundHttpRequest,
                Timestamp = DateTimeOffset.UtcNow,
                // Provider-mode calls keep their real path; "/mock" marks the prefixed surface.
                RequestLine = $"{request.Method} {(request.ProviderMode ? string.Empty : "/mock")}{request.Path}{request.QueryString}",
                RequestSnapshot = CallRecordSnapshot.Truncate(request.Body),
                ResponseSnapshot = CallRecordSnapshot.Truncate(result.ResponseBody),
                StatusCode = result.Matched ? 200 : 404,
                ContractValid = validation?.IsValid,
                ValidationErrors = validation is { IsValid: false } ? JsonSerializer.Serialize(validation.Errors) : null
            }, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Couldn't log mock call {Method} {Path} to the call history.", request.Method, request.Path);
        }

        return result;
    }

    /// <summary>HEAD is answered like GET (the server drops the body), so probes like "curl -I" work.</summary>
    private static string MatchMethod(string method) => string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase) ? "GET" : method;

    /// <summary>
    /// Null when there's nothing to check: no body (the spec doesn't record whether the body is
    /// required, so an absent one isn't a violation); a body that isn't JSON by its Content-Type
    /// (the stored schema is the spec's application/json one — a form post the spec also allows
    /// isn't a violation); or a body longer than the history keeps (validating a cut-off JSON
    /// document would only report it as malformed).
    /// </summary>
    private SchemaValidationResult? ValidateRequestBody(string schema, string? body, string? contentType)
    {
        if (string.IsNullOrWhiteSpace(body) || body.Length > CallRecordSnapshot.MaxLength || !IsJson(contentType))
        {
            return null;
        }

        return schemaValidator.Validate(schema, body);
    }

    /// <summary>"application/json", or any "+json" type ("application/problem+json"), ignoring parameters like charset.</summary>
    private static bool IsJson(string? contentType)
    {
        var mediaType = contentType?.Split(';', 2)[0].Trim();
        return mediaType is not null
            && (mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
                || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase));
    }
}
