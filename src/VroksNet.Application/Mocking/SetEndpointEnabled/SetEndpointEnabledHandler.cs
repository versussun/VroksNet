using Mediator;
using VroksNet.Application.Abstractions;
using VroksNet.Domain.Connections;
using VroksNet.Domain.TestScenarios;

namespace VroksNet.Application.Mocking.SetEndpointEnabled;

/// <summary>
/// Refused for an AsyncAPI operation: those aren't served by the mock, so the switch would mean
/// nothing. HTTP and gRPC operations can be switched; turning on a gRPC method another
/// specification already serves is refused (<see cref="RpcServingRules"/>). Turning an operation off
/// doesn't touch its provider-mode switch — it just stops answering until it's turned back on.
/// </summary>
public sealed class SetEndpointEnabledHandler(IApiSpecificationRepository specifications)
    : IRequestHandler<SetEndpointEnabled, SetEndpointEnabledResult>
{
    public async ValueTask<SetEndpointEnabledResult> Handle(SetEndpointEnabled request, CancellationToken cancellationToken)
    {
        var all = await specifications.ListAsync(cancellationToken);
        var endpoint = all.SelectMany(specification => specification.Endpoints).FirstOrDefault(e => e.Id == request.MockEndpointId);
        if (endpoint is null)
        {
            return new SetEndpointEnabledResult(Found: false);
        }

        if (OperationCompatibility.ShapeOf(endpoint.OperationKey) == OperationShape.Message)
        {
            return new SetEndpointEnabledResult(Found: true, "AsyncAPI operations aren't served by the mock, so they can't be turned on or off.");
        }

        if (request.Enabled && RpcServingRules.IsRpc(endpoint)
            && RpcServingRules.ServedElsewhere(endpoint.OperationKey, endpoint.SpecificationId, all) is { } served)
        {
            return new SetEndpointEnabledResult(Found: true, RpcServingRules.AlreadyServed(endpoint.OperationKey, served.SpecificationTitle));
        }

        var updated = await specifications.SetEnabledAsync([endpoint.Id], request.Enabled, cancellationToken);
        return updated == 1
            ? new SetEndpointEnabledResult(Found: true)
            : new SetEndpointEnabledResult(Found: true, ProviderModeRules.ChangedMeanwhile);
    }
}
