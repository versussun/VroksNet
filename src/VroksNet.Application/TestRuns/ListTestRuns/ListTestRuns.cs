using Mediator;
using VroksNet.Domain.TestRuns;

namespace VroksNet.Application.TestRuns.ListTestRuns;

/// <summary>
/// One page of the run history, newest first. <see cref="Cursor"/> is the previous page's
/// <see cref="TestRunPage.NextCursor"/> (null for the first page); <see cref="Limit"/> defaults to
/// 50 and is capped at 200.
/// </summary>
public sealed record ListTestRuns(
    Guid? TestScenarioId = null,
    TestRunStatus? Status = null,
    string? Cursor = null,
    int? Limit = null) : IRequest<TestRunPage>;
