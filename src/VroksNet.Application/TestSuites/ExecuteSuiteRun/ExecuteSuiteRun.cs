using Mediator;

namespace VroksNet.Application.TestSuites.ExecuteSuiteRun;

/// <summary>Runs one queued suite run — sent by the background worker.</summary>
public sealed record ExecuteSuiteRun(Guid SuiteRunId) : IRequest;
