using VroksNet.Domain.Connections;

namespace VroksNet.Domain.TestScenarios;

/// <summary>
/// Whether a <see cref="MockEndpoints.MockEndpoint.OperationKey"/> can be sent through a
/// <see cref="Connection"/> of a given <see cref="ConnectionServiceType"/>. OpenAPI operation
/// keys are always "METHOD /path" (contain a space); AsyncAPI ones are always
/// "channel/address:action" (no space) — that's the cheapest reliable way to tell them apart
/// without loading the specification's own <c>Kind</c>. An HTTP-shaped key needs an
/// <see cref="ConnectionServiceType.Http"/> connection; an AsyncAPI-shaped key needs a
/// RabbitMq/Nats one.
/// </summary>
public static class OperationCompatibility
{
    public static bool IsHttpOperation(string operationKey) => operationKey.Contains(' ');

    public static bool IsCompatible(string operationKey, ConnectionServiceType serviceType) =>
        IsHttpOperation(operationKey) == (serviceType == ConnectionServiceType.Http);
}
