using VroksNet.Domain.TestRuns;

namespace VroksNet.Application.Abstractions;

/// <summary>What <see cref="ITestRunRepository.CompleteAsync"/> records on a finished run.</summary>
public sealed record TestRunOutcome(
    TestRunStatus Status,
    DateTimeOffset FinishedAt,
    string Message,
    int? StatusCode = null,
    bool? ContractValid = null,
    string? ValidationErrors = null);
