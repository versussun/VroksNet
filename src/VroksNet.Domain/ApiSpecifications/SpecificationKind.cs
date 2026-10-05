namespace VroksNet.Domain.ApiSpecifications;

public enum SpecificationKind
{
    OpenApi,
    AsyncApi,

    /// <summary>A gRPC service described by .proto files (ADR 0004).</summary>
    Proto
}
