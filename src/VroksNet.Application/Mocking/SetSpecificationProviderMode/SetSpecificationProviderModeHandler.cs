using Mediator;
using VroksNet.Application.Abstractions;
using VroksNet.Domain.TestScenarios;

namespace VroksNet.Application.Mocking.SetSpecificationProviderMode;

/// <summary>
/// "Serve all at real paths": enables every HTTP operation of the specification that doesn't
/// overlap one already served — including one enabled earlier in this same pass, so a spec with
/// both "GET /pets/mine" and "GET /pets/{id}" gets the first (by key order) and reports the other
/// as skipped rather than failing outright. Disabling turns all of them off.
/// </summary>
public sealed class SetSpecificationProviderModeHandler(IApiSpecificationRepository specifications)
    : IRequestHandler<SetSpecificationProviderMode, SetSpecificationProviderModeResult?>
{
    public async ValueTask<SetSpecificationProviderModeResult?> Handle(SetSpecificationProviderMode request, CancellationToken cancellationToken)
    {
        var all = await specifications.ListAsync(cancellationToken);
        var specification = all.FirstOrDefault(s => s.Id == request.SpecificationId);
        if (specification is null)
        {
            return null;
        }

        var httpEndpoints = specification.Endpoints
            .Where(endpoint => OperationCompatibility.IsHttpOperation(endpoint.OperationKey))
            .OrderBy(endpoint => endpoint.OperationKey, StringComparer.Ordinal)
            .ToList();

        if (!request.Enabled)
        {
            var disabled = await specifications.SetServeAtRealPathAsync(httpEndpoints.Select(e => e.Id).ToList(), false, cancellationToken);
            return disabled == httpEndpoints.Count
                ? new SetSpecificationProviderModeResult([], [])
                : new SetSpecificationProviderModeResult([], [], ProviderModeRules.ChangedMeanwhile);
        }

        var served = ProviderModeRules.Served(all);
        var toEnable = new List<Guid>();
        var servedKeys = new List<string>();
        var skipped = new List<SkippedOperation>();

        foreach (var endpoint in httpEndpoints)
        {
            // Already served: it stays so, and it isn't "skipped" — whatever overlaps it was refused
            // when it tried to turn on.
            if (endpoint.ServeAtRealPath)
            {
                servedKeys.Add(endpoint.OperationKey);
                continue;
            }

            if (ProviderModeRules.RefusalReason(endpoint, served) is { } refusal)
            {
                skipped.Add(new SkippedOperation(endpoint.OperationKey, refusal));
                continue;
            }

            servedKeys.Add(endpoint.OperationKey);
            toEnable.Add(endpoint.Id);
            served.Add((endpoint, specification.Title));
        }

        var enabled = await specifications.SetServeAtRealPathAsync(toEnable, true, cancellationToken);
        return enabled == toEnable.Count
            ? new SetSpecificationProviderModeResult(servedKeys, skipped)
            : new SetSpecificationProviderModeResult(servedKeys, skipped, ProviderModeRules.ChangedMeanwhile);
    }
}
