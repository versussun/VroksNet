using Mediator;
using VroksNet.Application.Specifications.GetSpecificationDetails;
using VroksNet.Application.Specifications.ImportOpenApiSpec;
using VroksNet.Application.Specifications.ListSpecifications;

namespace VroksNet.ApiService.Endpoints;

/// <summary>
/// Every handler just maps HTTP to a Mediator request — no logic lives here, per
/// .claude/CLAUDE.md.
/// </summary>
public static class SpecificationEndpoints
{
    public static IEndpointRouteBuilder MapSpecificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/specifications");

        group.MapGet("/", async (IMediator mediator, CancellationToken cancellationToken) =>
        {
            var specifications = await mediator.Send(new ListSpecifications(), cancellationToken);
            return Results.Ok(specifications);
        })
        .WithName("ListSpecifications");

        group.MapGet("/{id:guid}", async (Guid id, IMediator mediator, CancellationToken cancellationToken) =>
        {
            var details = await mediator.Send(new GetSpecificationDetails(id), cancellationToken);
            return details is not null ? Results.Ok(details) : Results.NotFound();
        })
        .WithName("GetSpecificationDetails");

        group.MapPost("/openapi", async (HttpRequest request, IMediator mediator, CancellationToken cancellationToken) =>
        {
            using var reader = new StreamReader(request.Body);
            var yamlContent = await reader.ReadToEndAsync(cancellationToken);

            var fileName = request.Headers.TryGetValue("X-File-Name", out var headerValue)
                ? headerValue.ToString()
                : "spec.yaml";

            var id = await mediator.Send(new ImportOpenApiSpec(fileName, yamlContent), cancellationToken);

            return Results.Ok(new ImportSpecificationResult(id));
        })
        .WithName("ImportOpenApiSpecification")
        .Accepts<string>("text/plain");

        return app;
    }
}
