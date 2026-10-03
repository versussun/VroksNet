using Mediator;

namespace VroksNet.Application.TestRuns.QueueScheduledTestRuns;

/// <summary>
/// Sent by the background worker every tick: queues the next run of each scheduled scenario that
/// has none queued (ADR 0002). Result is how many runs it queued.
/// </summary>
public sealed record QueueScheduledTestRuns(DateTimeOffset Now) : IRequest<int>;
