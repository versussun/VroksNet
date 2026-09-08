namespace VroksNet.Application.Abstractions;

/// <summary>
/// Substitutes placeholders in a spec's example (from OpenAPI/AsyncAPI) with live values from
/// the matched request, before it's returned/published as a mock response. See
/// docs/project-brief.md section 2 "Динамика ответов" for the supported placeholders:
/// <c>{{request.path.*}}</c>, <c>{{request.query.*}}</c>, <c>{{request.header.*}}</c>,
/// <c>{{request.body.&lt;jsonpath&gt;}}</c>, <c>{{uuid}}</c>, <c>{{now}}</c>.
/// </summary>
/// <remarks>
/// Design-only for Phase 00 — real substitution logic (JSONPath body lookups etc.) belongs to
/// Phase 01's REST-mock work.
/// </remarks>
public interface IResponseTemplateEngine
{
    string Render(string template, TemplateContext context);
}

public sealed record TemplateContext(
    IReadOnlyDictionary<string, string> PathParameters,
    IReadOnlyDictionary<string, string> QueryParameters,
    IReadOnlyDictionary<string, string> Headers,
    string? Body);
