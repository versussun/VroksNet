using Mediator;

namespace VroksNet.Application.TestRuns.StartTestRun;

/// <summary>
/// Queues a background run of a scenario; the worker picks it up within a second of its time.
/// With <see cref="RunAt"/> or <see cref="DelaySeconds"/> (not both) it's a delayed one-off run
/// (ADR 0002), up to <see cref="StartTestRunHandler.MaxDelay"/> ahead; otherwise it runs now.
/// Result is the run's id, or null if no scenario with <see cref="TestScenarioId"/> exists.
/// </summary>
public sealed record StartTestRun(Guid TestScenarioId, DateTimeOffset? RunAt = null, int? DelaySeconds = null) : IRequest<Guid?>;
