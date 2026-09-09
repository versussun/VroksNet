namespace VroksNet.Application.Abstractions;

/// <summary>
/// Parses an AsyncAPI document specifically. A separate interface from <see cref="ISpecificationParser"/>
/// — rather than resolving that one generically — purely so DI can register an OpenAPI parser and
/// an AsyncAPI parser side by side without ambiguity: each kind's import handler asks for its own
/// kind by type instead of both racing for the same unqualified <see cref="ISpecificationParser"/>
/// registration.
/// </summary>
public interface IAsyncApiSpecificationParser : ISpecificationParser;
