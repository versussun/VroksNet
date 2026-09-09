using VroksNet.Application.Abstractions;
using VroksNet.Domain.TestScenarios;

namespace VroksNet.UnitTests.TestDoubles;

internal sealed class FakeTestScenarioRepository : ITestScenarioRepository
{
    private readonly List<TestScenario> _scenarios = [];

    public Task<IReadOnlyList<TestScenario>> ListAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<TestScenario>>(_scenarios.ToList());

    public Task<TestScenario?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
        => Task.FromResult(_scenarios.FirstOrDefault(s => s.Id == id));

    public Task InsertAsync(TestScenario scenario, CancellationToken cancellationToken)
    {
        _scenarios.Add(scenario);
        return Task.CompletedTask;
    }

    public Task<bool> UpdateAsync(TestScenario scenario, CancellationToken cancellationToken)
    {
        var index = _scenarios.FindIndex(s => s.Id == scenario.Id);
        if (index < 0)
        {
            return Task.FromResult(false);
        }

        _scenarios[index] = scenario;
        return Task.FromResult(true);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
        => Task.FromResult(_scenarios.RemoveAll(s => s.Id == id) > 0);
}
