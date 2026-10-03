using Mediator;

namespace VroksNet.Application.TestSuites.StartSuiteRun;

/// <summary>Queues a run of the suite (by id or name); the worker starts it within a second. Result is the suite run's id, or null if there's no such suite.</summary>
public sealed record StartSuiteRun(string Key) : IRequest<Guid?>;
