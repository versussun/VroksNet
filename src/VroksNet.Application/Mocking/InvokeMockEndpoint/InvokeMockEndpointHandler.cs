using System.Text.Json;
using Mediator;
using Microsoft.Extensions.Logging;
using VroksNet.Application.Abstractions;
using VroksNet.Application.CallRecords;
using VroksNet.Domain.CallRecords;

namespace VroksNet.Application.Mocking.InvokeMockEndpoint;

/// <summary>
/// Answers a mock call from the matching enabled endpoint's example — rendered as a template with
/// the request's values (docs/contract-testing-plan.md 4.6), at the status the spec gives it — and
/// logs it to the call history (<see cref="CallDirection.InboundHttpRequest"/>) — unmatched calls too, since "why did
/// my service get a 404?" is exactly what the history is for. Logging is best-effort: a failed
/// history write is logged as a warning and the mock still answers. A request body is checked
/// against the operation's request schema ("Type 3" in docs/contract-testing-plan.md) and the
/// outcome logged; the response doesn't change either way — the mock still answers the caller.
/// Placeholders that can't be filled in don't fail the call either: they're logged as warnings.
/// </summary>
public sealed class InvokeMockEndpointHandler(
    IApiSpecificationRepository repository,
    ICallRecordRepository callRecords,
    ISchemaValidator schemaValidator,
    IResponseTemplateEngine templateEngine,
    ILogger<InvokeMockEndpointHandler> logger)
    : IRequestHandler<InvokeMockEndpoint, MockInvocationResult>
{
    /// <summary>Stands in for an operation with no example — the spec gave none and the parser couldn't build one from a schema.</summary>
    private const string EmptyExampleBody = "{}";

    /// <summary>For an operation imported before its status was tracked, or one that declares no 2xx and gave no example.</summary>
    private const int DefaultStatusCode = 200;

    private const int StatusCodeNotFound = 404;

    public async ValueTask<MockInvocationResult> Handle(InvokeMockEndpoint request, CancellationToken cancellationToken)
    {
        var specifications = await repository.ListAsync(cancellationToken);

        IReadOnlyDictionary<string, string> pathParameters = new Dictionary<string, string>();
        var match = specifications
            .SelectMany(specification => specification.Endpoints)
            .Where(endpoint => endpoint.IsEnabled && (!request.ProviderMode || endpoint.ServeAtRealPath))
            .FirstOrDefault(endpoint => OperationKeyMatcher.TryMatch(endpoint.OperationKey, MatchMethod(request.Method), request.Path, out pathParameters));

        IReadOnlyList<string> warnings = [];
        MockInvocationResult result;
        if (match is null)
        {
            result = new MockInvocationResult(false, null, null, request.ProviderMode
                ? $"No operation is served at {request.Method} {request.Path} — turn on \"Serve at real path\" for it."
                : $"No enabled mock endpoint matches {request.Method} {request.Path}.", StatusCodeNotFound);
        }
        else
        {
            var statusCode = match.ExampleStatusCode ?? DefaultStatusCode;
            var body = EmptyExampleBody;
            if (!AllowsBody(statusCode))
            {
                body = string.Empty;
            }
            else if (match.ExampleTemplate is not null)
            {
                var rendered = templateEngine.Render(match.ExampleTemplate, new TemplateContext(
                    pathParameters,
                    request.QueryParameters ?? new Dictionary<string, string>(),
                    request.Headers ?? new Dictionary<string, string>(),
                    request.Body));
                body = rendered.Text;
                warnings = rendered.Warnings;
            }

            result = new MockInvocationResult(true, match.OperationKey, match.ExampleTemplate, body, statusCode);
        }

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
                StatusCode = result.StatusCode,
                ContractValid = validation?.IsValid,
                ValidationErrors = validation is { IsValid: false } ? JsonSerializer.Serialize(validation.Errors) : null,
                Warnings = warnings.Count > 0 ? JsonSerializer.Serialize(warnings) : null
            }, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Couldn't log mock call {Method} {Path} to the call history.", request.Method, request.Path);
        }

        return result;
    }

    /// <summary>1xx, 204 and 304 responses can't carry a body — the server would refuse to write one.</summary>
    private static bool AllowsBody(int statusCode) => statusCode is >= 200 and not 204 and not 304;

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
