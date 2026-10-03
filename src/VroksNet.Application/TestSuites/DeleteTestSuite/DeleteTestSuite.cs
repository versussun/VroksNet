using Mediator;

namespace VroksNet.Application.TestSuites.DeleteTestSuite;

/// <summary>Result is false if no suite with <see cref="Id"/> exists. Its run history stays, like a deleted scenario's.</summary>
public sealed record DeleteTestSuite(Guid Id) : IRequest<bool>;
