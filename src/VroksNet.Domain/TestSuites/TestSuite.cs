namespace VroksNet.Domain.TestSuites;

/// <summary>
/// A named list of test scenarios run together (ADR 0002, "Suites") — the unit a CI pipeline runs
/// and waits for. No foreign-key constraint to the scenarios, like a scenario's own references: a
/// deleted scenario stays listed and fails the suite's next run, rather than silently dropping out.
/// </summary>
public sealed class TestSuite
{
    public Guid Id { get; set; }

    /// <summary>Unique, so a pipeline can address the suite by name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The scenarios, in the order the suite was given them; Sends run in this order.</summary>
    public List<Guid> TestScenarioIds { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
