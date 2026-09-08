using VroksNet.Application.Abstractions;

namespace VroksNet.Infrastructure.Templating;

/// <summary>
/// Placeholder <see cref="IResponseTemplateEngine"/> for Phase 00 — returns the template
/// unchanged. Real placeholder substitution (<c>{{request.path.*}}</c>, <c>{{uuid}}</c>, etc.)
/// is Phase 01's REST-mock work; see docs/project-brief.md.
/// </summary>
public sealed class PassthroughResponseTemplateEngine : IResponseTemplateEngine
{
    public string Render(string template, TemplateContext context) => template;
}
