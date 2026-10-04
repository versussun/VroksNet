namespace VroksNet.Application.Abstractions;

/// <summary>Parses a raw OpenAPI or AsyncAPI YAML document into the minimal shape needed to store and mock it.</summary>
public interface ISpecificationParser
{
    Task<ParsedSpecification> ParseAsync(string rawContent, CancellationToken cancellationToken);
}

/// <summary>
/// One operation found in the spec (e.g. "GET /pets/{id}" for OpenAPI, or "orders.created:send"
/// for AsyncAPI), plus the example — pretty-printed JSON: the spec's own, or else one the parser
/// built from the operation's schema; null if neither exists — used as its mock response template.
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
/// <param name="ResponseSchemasByStatus">
/// OpenAPI only: every declared response keyed by its status key ("200", "4XX" — range keys
/// upper-cased — or "default"), mapped to that response's JSON body schema (self-contained the
/// same way), or null if it declares no JSON body. Null for AsyncAPI.
/// </param>
/// <param name="ExampleStatusCode">
/// OpenAPI only: the status the mock answers with — that of the response <see cref="ExampleJson"/>
/// was taken from ("2XX" counts as 200), or, when the example came from the request body or there
/// is none, the lowest declared 2xx. Null when neither applies (the mock then answers 200), and for AsyncAPI.
/// </param>
/// <param name="ExampleIsGenerated">The spec had no example: <see cref="ExampleJson"/> was built from the operation's schema.</param>
/// <param name="RequestExampleJson">
/// OpenAPI only: the request body an HTTP Send sends — the request body's own example, or one built
/// from <see cref="RequestSchemaJson"/>; null when the operation declares no JSON request body.
/// </param>
/// <param name="RequestExampleIsGenerated">The spec had no request example: <see cref="RequestExampleJson"/> was built from the request body's schema.</param>
public sealed record ParsedOperation(
    string OperationKey,
    string? ExampleJson,
    string? RequestSchemaJson = null,
    string? ResponseSchemaJson = null,
    IReadOnlyDictionary<string, string?>? ResponseSchemasByStatus = null,
    int? ExampleStatusCode = null,
    bool ExampleIsGenerated = false,
    string? RequestExampleJson = null,
    bool RequestExampleIsGenerated = false);

/// <summary>
/// Title (the version-matching key) plus its flat list of operations. <see cref="Protocols"/> is
/// AsyncAPI only: the distinct <c>servers.*.protocol</c> values, lower-case, in order.
/// </summary>
public sealed record ParsedSpecification(string Title, IReadOnlyList<ParsedOperation> Operations, IReadOnlyList<string>? Protocols = null);
