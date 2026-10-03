using Mediator;

namespace VroksNet.Application.TestRuns.SkipMissedScheduledTestRuns;

/// <summary>
/// Sent once at startup: scheduled runs still queued for a time that has passed were missed while
/// the app was down. They're cancelled rather than run late — missed runs aren't caught up
/// (ADR 0002). Result is how many.
/// </summary>
public sealed record SkipMissedScheduledTestRuns : IRequest<int>;
