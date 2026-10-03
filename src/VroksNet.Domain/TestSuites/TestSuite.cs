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

    /// <summary>Run it once each time the app starts — after provisioning, if there is any (ADR 0002, "Relation to ADR 0001").</summary>
    public bool RunOnStartup { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>When provisioning last brought this suite in line with the manifest; null for one created in the UI or the API.</summary>
    public DateTimeOffset? ProvisionedAt { get; set; }
}
