namespace VroksNet.Application.Abstractions;

/// <summary>
/// Substitutes placeholders in a spec's example (from OpenAPI/AsyncAPI) with live values from
/// the matched request, before it's returned as a mock response. See docs/project-brief.md
/// section 2 "Динамика ответов" for the supported placeholders: <c>{{request.path.*}}</c>,
/// <c>{{request.query.*}}</c>, <c>{{request.header.*}}</c>, <c>{{request.body.&lt;jsonpath&gt;}}</c>,
/// <c>{{uuid}}</c>, <c>{{now}}</c> — and docs/contract-testing-plan.md 3.9–3.10 for how values
/// are escaped and what an unresolvable placeholder becomes.
/// </summary>
public interface IResponseTemplateEngine
{
    TemplateRenderResult Render(string template, TemplateContext context);
}

/// <summary>
/// The request values placeholders can draw from. <see cref="QueryParameters"/> and
/// <see cref="Headers"/> hold repeated values comma-joined; header lookups ignore case.
/// <see cref="Body"/> is the raw request body, used only if it parses as JSON.
/// </summary>
public sealed record TemplateContext(
    IReadOnlyDictionary<string, string> PathParameters,
    IReadOnlyDictionary<string, string> QueryParameters,
    IReadOnlyDictionary<string, string> Headers,
    string? Body);

/// <summary>The rendered text, plus a UI-safe message for each placeholder that couldn't be filled in (it was replaced by null / an empty string instead).</summary>
public sealed record TemplateRenderResult(string Text, IReadOnlyList<string> Warnings);
