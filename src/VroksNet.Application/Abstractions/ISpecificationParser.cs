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
/// <param name="RequestSchemaJson">
/// The operation's request-body JSON Schema (OpenAPI only — self-contained, local `$ref`s already
/// inlined so it validates standalone), pretty-printed; null if the operation has no request body
/// (e.g. a GET) or the spec gave none. Always null for AsyncAPI — see <see cref="ResponseSchemaJson"/>.
/// </param>
/// <param name="ResponseSchemaJson">
/// For OpenAPI, the first JSON response's schema (the one <see cref="ExampleJson"/> was drawn
/// from) — self-contained the same way. For AsyncAPI, this instead carries the operation's
/// message payload schema (there's no request/response split for pub/sub; "the schema of what
/// you receive" is close enough in spirit to reuse this field rather than add a third one).
/// </param>
public sealed record ParsedOperation(string OperationKey, string? ExampleJson, string? RequestSchemaJson = null, string? ResponseSchemaJson = null);

/// <summary>Title (the version-matching key) plus its flat list of operations.</summary>
public sealed record ParsedSpecification(string Title, IReadOnlyList<ParsedOperation> Operations);
