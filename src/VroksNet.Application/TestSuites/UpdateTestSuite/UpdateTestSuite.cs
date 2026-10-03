using Mediator;

namespace VroksNet.Application.TestSuites.UpdateTestSuite;

/// <summary>Result is false if no suite with <see cref="Id"/> exists. Validation as in <see cref="CreateTestSuite.CreateTestSuite"/>.</summary>
public sealed record UpdateTestSuite(Guid Id, string Name, IReadOnlyList<Guid> TestScenarioIds) : IRequest<bool>;
