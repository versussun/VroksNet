using Mediator;
using VroksNet.Application.Abstractions;

namespace VroksNet.Application.Mocking.SetEndpointProviderMode;

/// <summary>
/// Turning provider mode on is refused for a non-HTTP operation, or one that would overlap an
/// operation already served at its real path (any specification) — so every request on the
/// provider port maps to exactly one operation. Turning it off always succeeds. The check and the
/// write aren't one transaction; two admins enabling overlapping operations at the same instant
/// could both succeed, which is acceptable for an admin-only toggle.
/// </summary>
public sealed class SetEndpointProviderModeHandler(IApiSpecificationRepository specifications)
    : IRequestHandler<SetEndpointProviderMode, SetEndpointProviderModeResult>
{
    public async ValueTask<SetEndpointProviderModeResult> Handle(SetEndpointProviderMode request, CancellationToken cancellationToken)
    {
        var all = await specifications.ListAsync(cancellationToken);
        var endpoint = all.SelectMany(specification => specification.Endpoints).FirstOrDefault(e => e.Id == request.MockEndpointId);
        if (endpoint is null)
        {
            return new SetEndpointProviderModeResult(Found: false);
        }

        if (request.Enabled && ProviderModeRules.RefusalReason(endpoint, ProviderModeRules.Served(all)) is { } refusal)
        {
            return new SetEndpointProviderModeResult(Found: true, refusal);
        }

        var updated = await specifications.SetServeAtRealPathAsync([endpoint.Id], request.Enabled, cancellationToken);
        return updated == 1
            ? new SetEndpointProviderModeResult(Found: true)
            : new SetEndpointProviderModeResult(Found: true, ProviderModeRules.ChangedMeanwhile);
    }
}
