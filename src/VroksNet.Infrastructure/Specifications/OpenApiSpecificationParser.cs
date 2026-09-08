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
/// spec's own example (if any) pretty-printed as JSON. Full schema-based example generation
/// (for operations with no example in the spec) is Phase 01 — see docs/project-brief.md.
/// </summary>
public sealed class OpenApiSpecificationParser : ISpecificationParser
{
    private static readonly JsonSerializerOptions ExampleJsonOptions = new() { WriteIndented = true };

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

        var operations = document.Paths?
            .SelectMany(pathItem => (pathItem.Value.Operations ?? [])
                .Select(operation => new ParsedOperation(
                    $"{operation.Key.ToString().ToUpperInvariant()} {pathItem.Key}",
                    ExtractExampleJson(operation.Value))))
            .ToList()
            ?? [];

        return new ParsedSpecification(title, operations);
    }

    /// <summary>Prefers the first (by status code) JSON response example, falling back to the request body's; null if the spec has neither.</summary>
    private static string? ExtractExampleJson(OpenApiOperation operation)
    {
        var example = operation.Responses?
            .OrderBy(response => response.Key, StringComparer.Ordinal)
            .Select(response => JsonExampleOf(response.Value.Content))
            .FirstOrDefault(value => value is not null)
            ?? JsonExampleOf(operation.RequestBody?.Content);

        return example?.ToJsonString(ExampleJsonOptions);
    }

    private static JsonNode? JsonExampleOf(IDictionary<string, IOpenApiMediaType>? content)
        => content is not null && content.TryGetValue("application/json", out var mediaType) ? mediaType.Example : null;
}
