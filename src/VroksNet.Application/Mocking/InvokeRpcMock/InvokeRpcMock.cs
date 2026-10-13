using Mediator;

namespace VroksNet.Application.Mocking.InvokeRpcMock;

/// <summary>
/// A call to the gRPC mock (ADR 0004): the method's path ("/package.Service/Method"), the request
/// messages the client sent (already out of their gRPC frames and decompressed — exactly one for a
/// unary method) and its metadata, which feeds the response's <c>{{request.header.*}}</c> placeholders.
/// </summary>
public sealed record InvokeRpcMock(
    string MethodPath,
    IReadOnlyList<byte[]> Messages,
    IReadOnlyDictionary<string, string>? Metadata = null) : IRequest<RpcMockResult>;
