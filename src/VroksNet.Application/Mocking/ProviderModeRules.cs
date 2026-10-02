using VroksNet.Domain.ApiSpecifications;
using VroksNet.Domain.MockEndpoints;
using VroksNet.Domain.TestScenarios;

namespace VroksNet.Application.Mocking;

/// <summary>Shared by the per-operation and whole-specification provider-mode handlers, so both refuse the same things.</summary>
internal static class ProviderModeRules
{
    /// <summary>Every operation currently served at its real path, across all specifications.</summary>
    public static List<(MockEndpoint Endpoint, string SpecificationTitle)> Served(IEnumerable<ApiSpecification> specifications)
        => specifications
            .SelectMany(specification => specification.Endpoints
                .Where(endpoint => endpoint.ServeAtRealPath)
                .Select(endpoint => (endpoint, specification.Title)))
            .ToList();

    /// <summary>Why <paramref name="candidate"/> can't be served at its real path, or null if it can.</summary>
    public static string? RefusalReason(MockEndpoint candidate, IEnumerable<(MockEndpoint Endpoint, string SpecificationTitle)> served)
    {
        if (!OperationCompatibility.IsHttpOperation(candidate.OperationKey))
        {
            return "Only HTTP operations can be served at a real path.";
        }

        var conflicts = served
            .Where(other => other.Endpoint.Id != candidate.Id && OperationOverlap.Overlaps(candidate.OperationKey, other.Endpoint.OperationKey))
            .Select(other => $"{other.Endpoint.OperationKey} in \"{other.SpecificationTitle}\"")
            .ToList();

        return conflicts.Count == 0
            ? null
            : $"It would overlap {string.Join(", ", conflicts)}, which is already served at its real path.";
    }

    /// <summary>The refusal when a write updated fewer endpoints than it should have.</summary>
    public const string ChangedMeanwhile = "The specification changed while this was being saved (it may have been re-imported) — reload and try again.";
}
