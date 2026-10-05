using VroksNet.Domain.Connections;

namespace VroksNet.Domain.TestScenarios;

/// <summary>
/// Whether a <see cref="MockEndpoints.MockEndpoint.OperationKey"/> can be sent through a
/// <see cref="Connection"/> of a given <see cref="ConnectionServiceType"/>. The key alone says what
/// kind of operation it is (<see cref="ShapeOf"/>) — the cheapest reliable way to tell, without
/// loading the specification's own <c>Kind</c>: gRPC keys start with "RPC " (ADR 0004), other
/// OpenAPI keys are "METHOD /path" (contain a space), and AsyncAPI ones are
/// "channel/address:action" (no space). A key needs a connection whose type has the same
/// <see cref="ServiceTypeTraits.OperationShape"/>.
/// </summary>
public static class OperationCompatibility
{
    /// <summary>How a gRPC operation key starts: "RPC /package.Service/Method".</summary>
    public const string RpcPrefix = "RPC ";

    public static OperationShape ShapeOf(string operationKey) =>
        operationKey.StartsWith(RpcPrefix, StringComparison.Ordinal) ? OperationShape.Rpc
        : operationKey.Contains(' ') ? OperationShape.Http
        : OperationShape.Message;

    public static bool IsHttpOperation(string operationKey) => ShapeOf(operationKey) == OperationShape.Http;

    /// <summary>False for an unknown <paramref name="serviceType"/> — nothing can be sent through it.</summary>
    public static bool IsCompatible(string operationKey, ConnectionServiceType serviceType) =>
        ServiceTypeTraits.Find(serviceType) is { } traits && ShapeOf(operationKey) == traits.OperationShape;

    /// <summary>AsyncAPI operation keys are "channel/address:action" — the channel address is everything before the last ':'; null if the key isn't AsyncAPI-shaped at all (HTTP or gRPC).</summary>
    public static string? ChannelAddressOf(string operationKey)
    {
        if (ShapeOf(operationKey) != OperationShape.Message)
        {
            return null;
        }

        var separatorIndex = operationKey.LastIndexOf(':');
        return separatorIndex > 0 ? operationKey[..separatorIndex] : null;
    }
}
