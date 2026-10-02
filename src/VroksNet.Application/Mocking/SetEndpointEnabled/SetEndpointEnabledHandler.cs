using Mediator;
using VroksNet.Application.Abstractions;
using VroksNet.Domain.TestScenarios;

namespace VroksNet.Application.Mocking.SetEndpointEnabled;

/// <summary>
/// Refused for a non-HTTP operation: AsyncAPI operations aren't served by the mock, so the switch
/// would mean nothing. Turning an operation off doesn't touch its provider-mode switch — it just
/// stops answering until it's turned back on.
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

        if (!OperationCompatibility.IsHttpOperation(endpoint.OperationKey))
        {
            return new SetEndpointEnabledResult(Found: true, "Only HTTP operations are served by the mock, so only they can be turned on or off.");
        }

        var updated = await specifications.SetEnabledAsync([endpoint.Id], request.Enabled, cancellationToken);
        return updated == 1
            ? new SetEndpointEnabledResult(Found: true)
            : new SetEndpointEnabledResult(Found: true, ProviderModeRules.ChangedMeanwhile);
    }
}
