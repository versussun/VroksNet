namespace VroksNet.Domain.MockEndpoints;

/// <summary>How a gRPC method streams (ADR 0004): which side sends a stream of messages rather than one.</summary>
public enum RpcKind
{
    Unary,
    ServerStreaming,
    ClientStreaming,
    BidiStreaming
}
