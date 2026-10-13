using VroksNet.Domain.ApiSpecifications;
using VroksNet.Domain.MockEndpoints;
using VroksNet.Domain.TestScenarios;

namespace VroksNet.Application.Mocking;

/// <summary>
/// gRPC methods answer at their real path on the gRPC port (ADR 0004) — clients can't put a prefix
/// in front of it — so at most one enabled operation may serve each method, across specifications.
/// Shared by the import (a newcomer that would collide comes in disabled), the "Mock enabled"
/// switch (turning on a second one is refused) and the mock itself.
/// </summary>
internal static class RpcServingRules
{
    /// <summary>The enabled operation of another specification that already serves <paramref name="operationKey"/>, with its spec's title; null if none.</summary>
    public static (MockEndpoint Endpoint, string SpecificationTitle)? ServedElsewhere(string operationKey, Guid? specificationId, IEnumerable<ApiSpecification> specifications)
        => specifications
            .Where(specification => specification.Id != specificationId)
            .SelectMany(specification => specification.Endpoints
                .Where(endpoint => endpoint.IsEnabled && endpoint.OperationKey == operationKey)
                .Select(endpoint => ((MockEndpoint Endpoint, string SpecificationTitle)?)(endpoint, specification.Title)))
            .FirstOrDefault();

    public static bool IsRpc(MockEndpoint endpoint) => OperationCompatibility.ShapeOf(endpoint.OperationKey) == Domain.Connections.OperationShape.Rpc;

    public static string AlreadyServed(string operationKey, string specificationTitle)
        => $"{operationKey[OperationCompatibility.RpcPrefix.Length..]} is already served by \"{specificationTitle}\" — turn that one off first.";
}
