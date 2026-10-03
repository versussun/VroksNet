using VroksNet.Domain.TestScenarios;

namespace VroksNet.Application.Provisioning;

/// <summary>A test scenario to create or bring in line by <see cref="Name"/>. A null <see cref="Kind"/> means the operation's default (Listen for an AsyncAPI "send", Send otherwise).</summary>
public sealed record ManifestTestScenario(
    string Name,
    string Specification,
    string Operation,
    string Connection,
    TestScenarioKind? Kind,
    int? ListenTimeoutSeconds,
    string? Exchange,
    string? PayloadOverride);
