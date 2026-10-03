using Mediator;

namespace VroksNet.Application.TestRuns.ListDueTestRuns;

/// <summary>The queued runs due at <see cref="Now"/>, oldest first — what the background worker should start.</summary>
public sealed record ListDueTestRuns(DateTimeOffset Now) : IRequest<IReadOnlyList<DueTestRun>>;
