namespace VroksNet.Application.Abstractions;

/// <summary>
/// What a gRPC Send needs besides the operation key (ADR 0004): the specification's stored
/// <c>FileDescriptorSet</c> and the method's message types, so the adapter can write the payload's
/// JSON as the request message through <see cref="IProtobufTranscoder"/>.
/// </summary>
public sealed record RpcSendContext(byte[] DescriptorSet, RpcMethodTypes Types);
