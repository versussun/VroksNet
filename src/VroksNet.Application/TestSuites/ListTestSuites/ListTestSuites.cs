using Mediator;

namespace VroksNet.Application.TestSuites.ListTestSuites;

public sealed record ListTestSuites : IRequest<IReadOnlyList<TestSuiteSummary>>;
