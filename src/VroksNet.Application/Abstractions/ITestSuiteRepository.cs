using VroksNet.Domain.TestSuites;

namespace VroksNet.Application.Abstractions;

public interface ITestSuiteRepository
{
    Task<IReadOnlyList<TestSuite>> ListAsync(CancellationToken cancellationToken);

    Task<TestSuite?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>By exact (already trimmed) name; names are unique.</summary>
    Task<TestSuite?> FindByNameAsync(string name, CancellationToken cancellationToken);

    Task InsertAsync(TestSuite suite, CancellationToken cancellationToken);

    /// <returns>False if the suite doesn't exist.</returns>
    Task<bool> UpdateAsync(TestSuite suite, CancellationToken cancellationToken);

    /// <returns>False if the suite doesn't exist.</returns>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}
