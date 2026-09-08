using System.Text;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using VroksNet.Application.Abstractions;

namespace VroksNet.Infrastructure.Specifications;

/// <summary>
/// Parses an OpenAPI 3.x YAML document via <c>Microsoft.OpenApi</c>/<c>Microsoft.OpenApi.YamlReader</c>
/// into a minimal <see cref="ParsedSpecification"/> (title + flat endpoint list). Full
/// schema/example modeling is Phase 01 — see docs/project-brief.md.
/// </summary>
public sealed class OpenApiSpecificationParser : ISpecificationParser
{
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

        var operationKeys = document.Paths?
            .SelectMany(pathItem => pathItem.Value.Operations?.Keys.Select(method => $"{method.ToString().ToUpperInvariant()} {pathItem.Key}") ?? [])
            .ToList()
            ?? [];

        return new ParsedSpecification(title, operationKeys);
    }
}
