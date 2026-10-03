using Mediator;

namespace VroksNet.Application.TestSuites.ListDueSuiteRuns;

/// <summary>The queued suite runs due at <see cref="Now"/>, oldest first — what the background worker should start.</summary>
public sealed record ListDueSuiteRuns(DateTimeOffset Now) : IRequest<IReadOnlyList<DueSuiteRun>>;
