namespace VroksNet.Application.Mocking.InvokeRpcMock;

/// <summary>The gRPC status codes the mock answers with (the numbers are gRPC's own, sent as <c>grpc-status</c>).</summary>
public enum RpcStatus
{
    Ok = 0,
    InvalidArgument = 3,
    DeadlineExceeded = 4,
    Unimplemented = 12,
    Internal = 13
}
