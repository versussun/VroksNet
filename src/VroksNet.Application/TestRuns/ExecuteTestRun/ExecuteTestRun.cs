using Mediator;

namespace VroksNet.Application.TestRuns.ExecuteTestRun;

/// <summary>Executes one queued run — sent by the background worker. A run that isn't queued any more (cancelled meanwhile) is left alone.</summary>
public sealed record ExecuteTestRun(Guid RunId) : IRequest;
