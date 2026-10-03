using Mediator;

namespace VroksNet.Application.TestRuns.StartTestRun;

/// <summary>Queues a background run of a scenario; the worker picks it up within a second. Result is the run's id, or null if no scenario with <see cref="TestScenarioId"/> exists.</summary>
public sealed record StartTestRun(Guid TestScenarioId) : IRequest<Guid?>;
