using VroksNet.Domain.TestScenarios;

namespace VroksNet.Application.Specifications.GetSpecificationDetails;

/// <summary>
/// One endpoint within a <see cref="SpecificationDetails"/> view, plus the Domain rules the Admin
/// UI needs for it so it doesn't re-derive them: <see cref="RequiresHttpConnection"/> (see
/// <see cref="OperationCompatibility"/>), <see cref="CanListen"/> and the mode a new Test Scenario
/// should start in (see <see cref="TestScenarioListening"/>).
/// </summary>
public sealed record MockEndpointDetail(
    Guid Id,
    string OperationKey,
    bool IsEnabled,
    string? ExampleTemplate,
    bool RequiresHttpConnection,
    bool CanListen,
    TestScenarioKind DefaultTestScenarioKind);
