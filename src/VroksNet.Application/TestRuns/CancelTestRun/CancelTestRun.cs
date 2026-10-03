using Mediator;

namespace VroksNet.Application.TestRuns.CancelTestRun;

/// <summary>Cancels a queued or running run.</summary>
public sealed record CancelTestRun(Guid RunId) : IRequest<CancelTestRunResult>;
