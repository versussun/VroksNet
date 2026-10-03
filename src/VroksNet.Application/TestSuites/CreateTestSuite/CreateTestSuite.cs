using Mediator;

namespace VroksNet.Application.TestSuites.CreateTestSuite;

/// <summary>A named list of scenarios run together. A blank or taken name, an empty list, a repeat or an unknown scenario throws <see cref="ArgumentException"/>.</summary>
public sealed record CreateTestSuite(string Name, IReadOnlyList<Guid> TestScenarioIds) : IRequest<Guid>;
