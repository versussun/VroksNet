using System.Text.Json;
using Mediator;
using Microsoft.Extensions.Logging;
using VroksNet.Application.Abstractions;
using VroksNet.Application.CallRecords;
using VroksNet.Domain.CallRecords;
using VroksNet.Domain.MockEndpoints;
using VroksNet.Domain.TestScenarios;

namespace VroksNet.Application.Mocking.InvokeRpcMock;

/// <summary>
/// Answers a call to the gRPC mock (ADR 0004) the way <c>InvokeMockEndpointHandler</c> answers an
/// HTTP one: the enabled operation for the method renders its example as a template (with the
/// decoded request as <c>{{request.body.*}}</c> and the metadata as <c>{{request.header.*}}</c>),
/// written as the response message. The request is decoded and checked against the request
/// message's schema — bytes that aren't that message are a violation too — and the outcome only goes
/// to the call history (<see cref="CallDirection.InboundGrpcCall"/>, unmatched calls included); the
/// answer never depends on it. Streaming methods aren't mocked yet (plan step G7).
/// </summary>
public sealed class InvokeRpcMockHandler(
    IApiSpecificationRepository repository,
    ICallRecordRepository callRecords,
    IProtobufTranscoder transcoder,
    ISchemaValidator schemaValidator,
    IResponseTemplateEngine templateEngine,
    ILogger<InvokeRpcMockHandler> logger) : IRequestHandler<InvokeRpcMock, RpcMockResult>
{
    private static readonly IReadOnlyDictionary<string, string> None = new Dictionary<string, string>();

    public async ValueTask<RpcMockResult> Handle(InvokeRpcMock request, CancellationToken cancellationToken)
    {
        var operationKey = OperationCompatibility.RpcPrefix + request.MethodPath;
        var specifications = await repository.ListAsync(cancellationToken);
        var candidates = specifications
            .SelectMany(specification => specification.Endpoints.Where(endpoint => endpoint.OperationKey == operationKey).Select(endpoint => (Specification: specification, Endpoint: endpoint)))
            .ToList();
        var match = candidates.FirstOrDefault(candidate => candidate.Endpoint.IsEnabled);

        var call = new Call();
        RpcMockResult result;
        if (match.Endpoint is null)
        {
            result = Status(RpcStatus.Unimplemented, candidates.Count > 0
                ? $"{request.MethodPath} is mocked but turned off."
                : $"{request.MethodPath} isn't mocked — import the .proto file that declares it.");
        }
        else if (match.Endpoint.RpcKind is not RpcKind.Unary)
        {
            result = Status(RpcStatus.Unimplemented, $"{request.MethodPath} is a streaming method, which the mock doesn't answer yet.");
        }
        else if (match.Specification.ProtoDescriptorSet is not { } descriptorSet || transcoder.FindMethod(descriptorSet, request.MethodPath) is not { } types)
        {
            result = Status(RpcStatus.Internal, $"{request.MethodPath}'s descriptors are missing — import its .proto file again.");
        }
        else
        {
            result = Answer(request, match.Endpoint, descriptorSet, types, call);
        }

        await RecordAsync(request, match.Endpoint, result, call, cancellationToken);
        return result;
    }

    private RpcMockResult Answer(InvokeRpcMock request, MockEndpoint endpoint, byte[] descriptorSet, RpcMethodTypes types, Call call)
    {
        // From here on the call is checked against the contract, so the history says whether it held.
        call.Validated = true;
        if (request.Messages.Count != 1)
        {
            call.Violations.Add($"A unary call carries exactly one request message; this one carried {request.Messages.Count}.");
            return Status(RpcStatus.Internal, $"Expected exactly one request message, got {request.Messages.Count}.");
        }

        var decoded = transcoder.ToJson(descriptorSet, types.RequestType, request.Messages[0]);
        call.Warnings.AddRange(decoded.Warnings);
        if (decoded.Json is null)
        {
            call.Violations.Add($"The request isn't a valid {types.RequestType}: {decoded.Error}");
            call.Request = $"({request.Messages[0].Length} bytes that aren't a valid {types.RequestType})";
        }
        else
        {
            call.Request = decoded.Json;
            if (endpoint.RequestSchema is { } schema)
            {
                call.Violations.AddRange(schemaValidator.Validate(schema, decoded.Json).Errors);
            }
        }

        var rendered = templateEngine.Render(endpoint.ExampleTemplate ?? "{}", new TemplateContext(None, None, request.Metadata ?? None, decoded.Json));
        call.Warnings.AddRange(rendered.Warnings);
        call.Response = rendered.Text;

        var encoded = transcoder.ToBinary(descriptorSet, types.ResponseType, rendered.Text);
        return encoded.Bytes is { } bytes
            ? new RpcMockResult(RpcStatus.Ok, null, bytes)
            : Status(RpcStatus.Internal, $"The example isn't a valid {types.ResponseType}: {encoded.Error}");
    }

    private async Task RecordAsync(InvokeRpcMock request, MockEndpoint? endpoint, RpcMockResult result, Call call, CancellationToken cancellationToken)
    {
        try
        {
            await callRecords.InsertAsync(new CallRecord
            {
                Id = Guid.NewGuid(),
                SpecificationId = endpoint?.SpecificationId,
                MockEndpointId = endpoint?.Id,
                Direction = CallDirection.InboundGrpcCall,
                Timestamp = DateTimeOffset.UtcNow,
                RequestLine = OperationCompatibility.RpcPrefix + request.MethodPath,
                RequestSnapshot = CallRecordSnapshot.Truncate(call.Request),
                ResponseSnapshot = CallRecordSnapshot.Truncate(result.Status == RpcStatus.Ok ? call.Response : result.Message),
                StatusCode = (int)result.Status,
                ContractValid = call.Validated ? call.Violations.Count == 0 : null,
                ValidationErrors = call.Validated && call.Violations.Count > 0 ? JsonSerializer.Serialize(call.Violations) : null,
                Warnings = call.Warnings.Count > 0 ? JsonSerializer.Serialize(call.Warnings) : null
            }, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Couldn't log gRPC mock call {Method} to the call history.", request.MethodPath);
        }
    }

    private static RpcMockResult Status(RpcStatus status, string message) => new(status, message, null);

    /// <summary>What a call collects on its way, for the history.</summary>
    private sealed class Call
    {
        public string? Request { get; set; }

        public string? Response { get; set; }

        public bool Validated { get; set; }

        public List<string> Violations { get; } = [];

        public List<string> Warnings { get; } = [];
    }
}
