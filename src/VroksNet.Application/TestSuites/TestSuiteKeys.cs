using VroksNet.Application.Abstractions;
using VroksNet.Domain.TestSuites;

namespace VroksNet.Application.TestSuites;

/// <summary>A suite is addressed by id or by name — a CI pipeline knows the name.</summary>
internal static class TestSuiteKeys
{
    public static async Task<TestSuite?> FindAsync(ITestSuiteRepository suites, string key, CancellationToken cancellationToken)
        => Guid.TryParse(key, out var id)
            ? await suites.FindByIdAsync(id, cancellationToken)
            : await suites.FindByNameAsync(key.Trim(), cancellationToken);
}
