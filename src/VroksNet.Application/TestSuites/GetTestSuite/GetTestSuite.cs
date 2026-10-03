using Mediator;

namespace VroksNet.Application.TestSuites.GetTestSuite;

/// <summary>A suite by id or name; null if there's none.</summary>
public sealed record GetTestSuite(string Key) : IRequest<TestSuiteSummary?>;
