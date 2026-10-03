namespace VroksNet.Application.TestRuns;

/// <summary>A page of the run history, newest first. <see cref="NextCursor"/> fetches the next (older) page; null when this is the last.</summary>
public sealed record TestRunPage(IReadOnlyList<TestRunSummary> Items, string? NextCursor);
