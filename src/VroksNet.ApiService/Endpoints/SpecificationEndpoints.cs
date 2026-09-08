using Mediator;
using VroksNet.Application.Specifications.ImportOpenApiSpec;

namespace VroksNet.ApiService.Endpoints;

/// <summary>
/// Dev-facing endpoint proving the OpenAPI parsing prototype end-to-end (see
/// docs/project-brief.md section 6). Every handler just maps HTTP to a Mediator request — no
/// logic lives here, per .claude/CLAUDE.md.
/// </summary>
public static class SpecificationEndpoints
{
    public static IEndpointRouteBuilder MapSpecificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/specifications");

        group.MapPost("/openapi", async (HttpRequest request, IMediator mediator, CancellationToken cancellationToken) =>
        {
            using var reader = new StreamReader(request.Body);
            var yamlContent = await reader.ReadToEndAsync(cancellationToken);

            var fileName = request.Headers.TryGetValue("X-File-Name", out var headerValue)
                ? headerValue.ToString()
                : "spec.yaml";

            var id = await mediator.Send(new ImportOpenApiSpec(fileName, yamlContent), cancellationToken);

            return Results.Ok(new { id });
        })
        .WithName("ImportOpenApiSpecification")
        .Accepts<string>("text/plain");

        return app;
    }
}
