namespace VroksNet.Application.Mocking.InvokeRpcMock;

/// <summary>What the gRPC mock answers: a status (with its message, for anything but OK) and, when OK, the response message's bytes.</summary>
public sealed record RpcMockResult(RpcStatus Status, string? Message, byte[]? Response);
