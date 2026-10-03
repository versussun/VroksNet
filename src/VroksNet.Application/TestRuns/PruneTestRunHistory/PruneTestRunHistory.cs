using Mediator;

namespace VroksNet.Application.TestRuns.PruneTestRunHistory;

/// <summary>Keeps the newest <see cref="KeepPerScenario"/> finished runs of each scenario and deletes the rest (ADR 0002: 100 by default). Result is how many were deleted.</summary>
public sealed record PruneTestRunHistory(int KeepPerScenario) : IRequest<int>;
