using VroksNet.Application.Abstractions;

namespace VroksNet.Application.TestSuites;

/// <summary>The scenario list create/update accept: at least one, no repeats, and every one existing.</summary>
internal static class TestSuiteScenarios
{
    /// <summary>Throws <see cref="ArgumentException"/> (the endpoints' 400) with the reason.</summary>
    public static async Task<List<Guid>> ValidateAsync(ITestScenarioRepository scenarios, IReadOnlyList<Guid>? ids, CancellationToken cancellationToken)
    {
        if (ids is not { Count: > 0 })
        {
            throw new ArgumentException("A test suite needs at least one test scenario.");
        }

        if (ids.Distinct().Count() != ids.Count)
        {
            throw new ArgumentException("A test scenario can only be in a suite once.");
        }

        var existing = (await scenarios.ListAsync(cancellationToken)).Select(scenario => scenario.Id).ToHashSet();
        var unknown = ids.Where(id => !existing.Contains(id)).ToList();
        if (unknown.Count > 0)
        {
            throw new ArgumentException($"No test scenario with id {string.Join(", ", unknown)}.");
        }

        return [.. ids];
    }
}
