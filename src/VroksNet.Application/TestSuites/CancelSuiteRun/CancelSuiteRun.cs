using Mediator;
using VroksNet.Application.TestRuns.CancelTestRun;

namespace VroksNet.Application.TestSuites.CancelSuiteRun;

/// <summary>Cancels a queued suite run, or stops a running one together with its runs.</summary>
public sealed record CancelSuiteRun(Guid Id) : IRequest<CancelTestRunResult>;
