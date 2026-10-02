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
/// history write is logged as a warning and the mock still answers.
/// </summary>
public sealed class InvokeMockEndpointHandler(
    IApiSpecificationRepository repository,
    ICallRecordRepository callRecords,
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
            .Where(endpoint => endpoint.IsEnabled)
            .FirstOrDefault(endpoint => OperationKeyMatcher.Matches(endpoint.OperationKey, request.Method, request.Path));

        var result = match is null
            ? new MockInvocationResult(false, null, null, $"No enabled mock endpoint matches {request.Method} {request.Path}.")
            : new MockInvocationResult(true, match.OperationKey, match.ExampleTemplate, match.ExampleTemplate ?? EmptyExampleBody);

        try
        {
            await callRecords.InsertAsync(new CallRecord
            {
                Id = Guid.NewGuid(),
                SpecificationId = match?.SpecificationId,
                MockEndpointId = match?.Id,
                Direction = CallDirection.InboundHttpRequest,
                Timestamp = DateTimeOffset.UtcNow,
                RequestLine = $"{request.Method} /mock{request.Path}{request.QueryString}",
                RequestSnapshot = CallRecordSnapshot.Truncate(request.Body),
                ResponseSnapshot = CallRecordSnapshot.Truncate(result.ResponseBody),
                StatusCode = result.Matched ? 200 : 404
            }, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Couldn't log mock call {Method} {Path} to the call history.", request.Method, request.Path);
        }

        return result;
    }
}
