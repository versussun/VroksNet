using Mediator;

namespace VroksNet.Application.TestSuites.ListSuiteRuns;

/// <summary>A suite's (by id or name) newest runs, newest first; null if there's no such suite. <c>Limit</c> 1 is "the latest run" a pipeline polls.</summary>
public sealed record ListSuiteRuns(string Key, int Limit = 20) : IRequest<IReadOnlyList<SuiteRunDetails>?>;
