using VroksNet.Domain.TestRuns;

namespace VroksNet.Application.Abstractions;

/// <summary>Narrows a <see cref="ITestRunRepository.ListAsync"/> page; every null criterion matches everything.</summary>
public sealed record TestRunFilter(Guid? TestScenarioId = null, TestRunStatus? Status = null);
