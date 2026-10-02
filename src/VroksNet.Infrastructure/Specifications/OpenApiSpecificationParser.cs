using System.Globalization;
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
/// docs/contract-testing-plan.md, and the status the mock answers with. There's no schema-based
/// example generation for operations with no example in the spec — the mock answers those "{}".
/// </summary>
public sealed class OpenApiSpecificationParser : ISpecificationParser
{
    private static readonly JsonSerializerOptions ExampleJsonOptions = new() { WriteIndented = true };

    // Inlines "$ref": "#/components/schemas/X" into the schema's own serialized text, so a schema
    // extracted for one operation validates standalone — the rest of the document isn't kept
    // around as context once RequestSchemaJson/ResponseSchemaJson are stored on MockEndpoint.
    private static readonly OpenApiWriterSettings SchemaWriterSettings = new() { InlineLocalReferences = true };

    // A recursive schema can't be fully inlined: Microsoft.OpenApi inlines one level and leaves
    // "$ref": "#/components/schemas/X" at the cycle. Those components are copied into the
    // extracted schema's own "$defs" (serialized with their refs as-is) and every such ref is
    // rewritten to "#/$defs/X", so the schema still resolves standalone.
    private static readonly OpenApiWriterSettings ComponentWriterSettings = new();

    private const string ComponentSchemaRefPrefix = "#/components/schemas/";

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
                    operations.Add(await ParseOperationAsync(operationKey, operation.Value, document.Components?.Schemas, cancellationToken));
                }
            }
        }

        return new ParsedSpecification(title, operations);
    }

    private static async Task<ParsedOperation> ParseOperationAsync(
        string operationKey,
        OpenApiOperation operation,
        IDictionary<string, IOpenApiSchema>? componentSchemas,
        CancellationToken cancellationToken)
    {
        var requestMediaType = JsonMediaTypeOf(operation.RequestBody?.Content);
        var (firstJsonStatusKey, firstJsonResponse) = operation.Responses?
            .OrderBy(response => response.Key, StringComparer.Ordinal)
            .Select(response => (response.Key, MediaType: JsonMediaTypeOf(response.Value.Content)))
            .FirstOrDefault(response => response.MediaType is not null) ?? default;

        // Prefers the first (by status code) JSON response example, falling back to the request
        // body's; null if the spec has neither. The mock answers with the status of the response
        // the example came from — otherwise (no example, or the request body's) with the lowest
        // declared 2xx (docs/contract-testing-plan.md 3.8).
        var example = firstJsonResponse?.Example ?? requestMediaType?.Example;
        var exampleStatusCode = (firstJsonResponse?.Example is not null ? StatusCodeOf(firstJsonStatusKey!) : null)
            ?? LowestSuccessStatusCode(operation.Responses?.Keys);

        // Every declared response, not just the first — a contract check has to validate whatever
        // status the real service actually returns against that status's own schema.
        var responseSchemasByStatus = new Dictionary<string, string?>();
        foreach (var response in operation.Responses ?? [])
        {
            var statusKey = response.Key.Equals("default", StringComparison.OrdinalIgnoreCase) ? "default" : response.Key.ToUpperInvariant();
            responseSchemasByStatus[statusKey] = await ExtractSchemaJsonAsync(JsonMediaTypeOf(response.Value.Content)?.Schema, componentSchemas, cancellationToken);
        }

        return new ParsedOperation(
            operationKey,
            example?.ToJsonString(ExampleJsonOptions),
            await ExtractSchemaJsonAsync(requestMediaType?.Schema, componentSchemas, cancellationToken),
            await ExtractSchemaJsonAsync(firstJsonResponse?.Schema, componentSchemas, cancellationToken),
            responseSchemasByStatus,
            exampleStatusCode);
    }

    /// <summary>"201" → 201, "2XX" → 200; null for "default" or anything unrecognizable.</summary>
    private static int? StatusCodeOf(string statusKey)
    {
        if (int.TryParse(statusKey, NumberStyles.None, CultureInfo.InvariantCulture, out var code) && code is >= 100 and <= 599)
        {
            return code;
        }

        return statusKey is [>= '1' and <= '5', 'X' or 'x', 'X' or 'x'] ? (statusKey[0] - '0') * 100 : null;
    }

    private static int? LowestSuccessStatusCode(IEnumerable<string>? statusKeys)
        => statusKeys?
            .Select(StatusCodeOf)
            .Where(code => code is >= 200 and < 300)
            .Min();

    private static IOpenApiMediaType? JsonMediaTypeOf(IDictionary<string, IOpenApiMediaType>? content)
        => content is not null && content.TryGetValue("application/json", out var mediaType) ? mediaType : null;

    private static async Task<string?> ExtractSchemaJsonAsync(
        IOpenApiSchema? schema,
        IDictionary<string, IOpenApiSchema>? componentSchemas,
        CancellationToken cancellationToken)
    {
        if (schema is null)
        {
            return null;
        }

        var root = await SerializeSchemaAsync(schema, SchemaWriterSettings, cancellationToken);

        var pending = new Queue<string>(RewriteComponentRefs(root));
        if (pending.Count > 0 && root is JsonObject rootObject)
        {
            var defs = new JsonObject();
            while (pending.TryDequeue(out var name))
            {
                // A ref to a component that doesn't exist stays unresolved — SchemaValidator
                // reports that as a violation instead of throwing.
                if (defs.ContainsKey(name) || componentSchemas is null || !componentSchemas.TryGetValue(name, out var component))
                {
                    continue;
                }

                var definition = await SerializeSchemaAsync(component, ComponentWriterSettings, cancellationToken);
                defs[name] = definition;
                foreach (var referenced in RewriteComponentRefs(definition))
                {
                    pending.Enqueue(referenced);
                }
            }

            rootObject["$defs"] = defs;
        }

        // Re-serialized indented, purely for readability when inspected/debugged — the same
        // treatment ExampleJsonOptions already gives the example JSON above.
        return root?.ToJsonString(ExampleJsonOptions);
    }

    private static async Task<JsonNode?> SerializeSchemaAsync(IOpenApiSchema schema, OpenApiWriterSettings settings, CancellationToken cancellationToken)
    {
        using var stringWriter = new StringWriter();
        var writer = new OpenApiJsonWriter(stringWriter, settings);
        schema.SerializeAsV31(writer);
        await writer.FlushAsync(cancellationToken);
        return JsonNode.Parse(stringWriter.ToString());
    }

    /// <summary>Rewrites every "#/components/schemas/X" ref under <paramref name="node"/> to "#/$defs/X", returning the component names it pointed at.</summary>
    private static List<string> RewriteComponentRefs(JsonNode? node)
    {
        var names = new List<string>();
        Visit(node);
        return names;

        void Visit(JsonNode? current)
        {
            switch (current)
            {
                case JsonObject obj:
                    if (obj["$ref"] is JsonValue refValue
                        && refValue.TryGetValue<string>(out var reference)
                        && reference.StartsWith(ComponentSchemaRefPrefix, StringComparison.Ordinal))
                    {
                        var name = reference[ComponentSchemaRefPrefix.Length..];
                        names.Add(name);
                        obj["$ref"] = $"#/$defs/{name}";
                    }

                    foreach (var property in obj.ToList())
                    {
                        Visit(property.Value);
                    }

                    break;

                case JsonArray array:
                    foreach (var item in array)
                    {
                        Visit(item);
                    }

                    break;
            }
        }
    }
}
