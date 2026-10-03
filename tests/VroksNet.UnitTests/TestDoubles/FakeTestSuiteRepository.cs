using VroksNet.Application.Abstractions;
using VroksNet.Domain.TestSuites;

namespace VroksNet.UnitTests.TestDoubles;

internal sealed class FakeTestSuiteRepository : ITestSuiteRepository
{
    private readonly List<TestSuite> _suites = [];

    public Task<IReadOnlyList<TestSuite>> ListAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<TestSuite>>(_suites.OrderBy(suite => suite.Name).ToList());

    public Task<TestSuite?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
        => Task.FromResult(_suites.FirstOrDefault(suite => suite.Id == id));

    public Task<TestSuite?> FindByNameAsync(string name, CancellationToken cancellationToken)
        => Task.FromResult(_suites.FirstOrDefault(suite => suite.Name == name));

    public Task InsertAsync(TestSuite suite, CancellationToken cancellationToken)
    {
        _suites.Add(suite);
        return Task.CompletedTask;
    }

    public Task<bool> UpdateAsync(TestSuite suite, CancellationToken cancellationToken)
    {
        var index = _suites.FindIndex(s => s.Id == suite.Id);
        if (index >= 0)
        {
            _suites[index] = suite;
        }

        return Task.FromResult(index >= 0);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
        => Task.FromResult(_suites.RemoveAll(suite => suite.Id == id) > 0);
}
