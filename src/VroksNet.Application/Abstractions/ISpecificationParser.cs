namespace VroksNet.Application.Abstractions;

/// <summary>Parses a raw OpenAPI or AsyncAPI YAML document into the minimal shape needed to store and mock it.</summary>
public interface ISpecificationParser
{
    Task<ParsedSpecification> ParseAsync(string rawContent, CancellationToken cancellationToken);
}

/// <summary>
/// One operation found in the spec (e.g. "GET /pets/{id}" for OpenAPI, or "orders.created:send"
/// for AsyncAPI), plus the example — pretty-printed JSON, if the spec had one — used as its mock
/// response template.
/// </summary>
public sealed record ParsedOperation(string OperationKey, string? ExampleJson);

/// <summary>Title (the version-matching key) plus its flat list of operations.</summary>
public sealed record ParsedSpecification(string Title, IReadOnlyList<ParsedOperation> Operations);
