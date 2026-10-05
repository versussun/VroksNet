namespace VroksNet.Domain.Connections;

/// <summary>
/// What kind of operation a <see cref="MockEndpoints.MockEndpoint.OperationKey"/> describes, and so
/// which connection types can carry it (<see cref="ServiceTypeTraits.OperationShape"/>). Read from
/// the key by <see cref="TestScenarios.OperationCompatibility.ShapeOf"/>.
/// </summary>
public enum OperationShape
{
    /// <summary>An OpenAPI request: "METHOD /path".</summary>
    Http,

    /// <summary>An AsyncAPI operation on a broker channel: "channel/address:action".</summary>
    Message,

    /// <summary>A gRPC method from a .proto file: "RPC /package.Service/Method" (ADR 0004).</summary>
    Rpc
}
