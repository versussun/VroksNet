using Mediator;

namespace VroksNet.Application.TestRuns.GetTestRun;

/// <summary>Result is null if no run with <see cref="Id"/> exists.</summary>
public sealed record GetTestRun(Guid Id) : IRequest<TestRunSummary?>;
