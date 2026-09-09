using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using VroksNet.Application.Abstractions;

namespace VroksNet.Infrastructure.Specifications;

/// <summary>
/// Parses an OpenAPI 3.x YAML document via <c>Microsoft.OpenApi</c>/<c>Microsoft.OpenApi.YamlReader</c>
/// into a minimal <see cref="ParsedSpecification"/> — a flat operation list, each carrying the
/// spec's own example (if any) pretty-printed as JSON, plus its request/response JSON Schemas
/// (see <see cref="ParsedOperation"/>) for the contract-testing checks described in
/// docs/contract-testing-plan.md. Full schema-based example generation (for operations with no
/// example in the spec) is Phase 01 — see docs/project-brief.md.
/// </summary>
public sealed class OpenApiSpecificationParser : ISpecificationParser
{
    private static readonly JsonSerializerOptions ExampleJsonOptions = new() { WriteIndented = true };

    // Inlines "$ref": "#/components/schemas/X" into the schema's own serialized text, so a schema
    // extracted for one operation validates standalone — the rest of the document isn't kept
    // around as context once RequestSchemaJson/ResponseSchemaJson are stored on MockEndpoint.
    private static readonly OpenApiWriterSettings SchemaWriterSettings = new() { InlineLocalReferences = true };

    public async Task<ParsedSpecification> ParseAsync(string rawContent, CancellationToken cancellationToken)
    {
        var settings = new OpenApiReaderSettings();
        settings.AddYamlReader();

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(rawContent));
        var result = await OpenApiDocument.LoadAsync(stream, "yaml", settings, cancellationToken);

        if (result.Document is null)
        {
            var errors = string.Join("; ", result.Diagnostic?.Errors.Select(e => e.Message) ?? []);
            throw new InvalidOperationException($"Failed to parse OpenAPI document: {errors}");
        }

        var document = result.Document;
        var title = document.Info?.Title ?? "(untitled)";

        var operations = new List<ParsedOperation>();
        if (document.Paths is not null)
        {
            foreach (var pathItem in document.Paths)
            {
                foreach (var operation in pathItem.Value.Operations ?? [])
                {
                    var operationKey = $"{operation.Key.ToString().ToUpperInvariant()} {pathItem.Key}";
                    operations.Add(await ParseOperationAsync(operationKey, operation.Value, cancellationToken));
                }
            }
        }

        return new ParsedSpecification(title, operations);
    }

    private static async Task<ParsedOperation> ParseOperationAsync(string operationKey, OpenApiOperation operation, CancellationToken cancellationToken)
    {
        var requestMediaType = JsonMediaTypeOf(operation.RequestBody?.Content);
        var firstJsonResponse = operation.Responses?
            .OrderBy(response => response.Key, StringComparer.Ordinal)
            .Select(response => JsonMediaTypeOf(response.Value.Content))
            .FirstOrDefault(mediaType => mediaType is not null);

        // Prefers the first (by status code) JSON response example, falling back to the request
        // body's; null if the spec has neither.
        var example = firstJsonResponse?.Example ?? requestMediaType?.Example;

        return new ParsedOperation(
            operationKey,
            example?.ToJsonString(ExampleJsonOptions),
            await ExtractSchemaJsonAsync(requestMediaType?.Schema, cancellationToken),
            await ExtractSchemaJsonAsync(firstJsonResponse?.Schema, cancellationToken));
    }

    private static IOpenApiMediaType? JsonMediaTypeOf(IDictionary<string, IOpenApiMediaType>? content)
        => content is not null && content.TryGetValue("application/json", out var mediaType) ? mediaType : null;

    private static async Task<string?> ExtractSchemaJsonAsync(IOpenApiSchema? schema, CancellationToken cancellationToken)
    {
        if (schema is null)
        {
            return null;
        }

        using var stringWriter = new StringWriter();
        var writer = new OpenApiJsonWriter(stringWriter, SchemaWriterSettings);
        schema.SerializeAsV31(writer);
        await writer.FlushAsync(cancellationToken);

        // Re-parse + re-serialize indented, purely for readability when inspected/debugged —
        // the same treatment ExampleJsonOptions already gives the example JSON above.
        return JsonNode.Parse(stringWriter.ToString())?.ToJsonString(ExampleJsonOptions);
    }
}
