using VroksNet.Domain.TestScenarios;

namespace VroksNet.Application.Abstractions;

public interface ITestScenarioRepository
{
    Task<IReadOnlyList<TestScenario>> ListAsync(CancellationToken cancellationToken);

    Task<TestScenario?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The one with exactly this name (names are unique, compared as stored — trimmed, case-sensitive); null if none.</summary>
    Task<TestScenario?> FindByNameAsync(string name, CancellationToken cancellationToken);

    Task InsertAsync(TestScenario scenario, CancellationToken cancellationToken);

    /// <returns>False if no scenario with <see cref="TestScenario.Id"/> exists.</returns>
    Task<bool> UpdateAsync(TestScenario scenario, CancellationToken cancellationToken);

    /// <returns>False if no scenario with that id exists.</returns>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Records the outcome of a <c>Run</c> — <see cref="TestScenario.LastRunAt"/>/<see cref="TestScenario.LastRunSuccess"/>/<see cref="TestScenario.LastRunMessage"/>
    /// only, deliberately separate from <see cref="UpdateAsync"/> (which is about edits to the
    /// scenario's own definition, not about runs of it).
    /// </summary>
    Task RecordRunAsync(Guid id, DateTimeOffset ranAt, bool success, string message, CancellationToken cancellationToken);
}
