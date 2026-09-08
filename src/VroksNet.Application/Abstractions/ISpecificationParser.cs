namespace VroksNet.Application.Abstractions;

/// <summary>Parses a raw OpenAPI or AsyncAPI YAML document into the minimal shape needed to store and mock it.</summary>
public interface ISpecificationParser
{
    Task<ParsedSpecification> ParseAsync(string rawContent, CancellationToken cancellationToken);
}

/// <summary>
/// Title (the version-matching key) plus a flat list of operation keys (e.g. "GET /pets/{id}"
/// or "orders.created:send"). Full schema/example modeling is Phase 01 — see
/// docs/project-brief.md.
/// </summary>
public sealed record ParsedSpecification(string Title, IReadOnlyList<string> OperationKeys);
