using Mediator;
using VroksNet.ApiService.Endpoints.Requests;
using VroksNet.Application.Mocking.SetSpecificationProviderMode;
using VroksNet.Application.Specifications.GetSpecificationDetails;
using VroksNet.Application.Specifications.ImportAsyncApiSpec;
using VroksNet.Application.Specifications.ImportOpenApiSpec;
using VroksNet.Application.Specifications.ListSpecifications;
using VroksNet.ApiService.Endpoints.Responses;

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

        // Body: { "enabled": true|false }. Enabling serves every HTTP operation that doesn't overlap
        // one already served and reports the rest as skipped, with why.
        group.MapPut("/{id:guid}/provider-mode", async (Guid id, ProviderModeBody body, IMediator mediator, CancellationToken cancellationToken) =>
        {
            if (body.Enabled is not { } enabled)
            {
                return Results.Problem(detail: "The body must say { \"enabled\": true } or { \"enabled\": false }.", statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await mediator.Send(new SetSpecificationProviderMode(id, enabled), cancellationToken);
            return result switch
            {
                null => Results.NotFound(),
                { Refusal: { } refusal } => Results.Problem(detail: refusal, statusCode: StatusCodes.Status409Conflict),
                _ => Results.Ok(result)
            };
        })
        .WithName("SetSpecificationProviderMode");

        group.MapPost("/openapi", async (HttpRequest request, IMediator mediator, CancellationToken cancellationToken) =>
        {
            using var reader = new StreamReader(request.Body);
            var yamlContent = await reader.ReadToEndAsync(cancellationToken);

            var fileName = request.Headers.TryGetValue("X-File-Name", out var headerValue)
                ? headerValue.ToString()
                : "spec.yaml";

            try
            {
                var id = await mediator.Send(new ImportOpenApiSpec(fileName, yamlContent), cancellationToken);
                return Results.Ok(new ImportSpecificationResult(id));
            }
            catch (InvalidOperationException ex)
            {
                // OpenApiSpecificationParser throws this for a file that doesn't parse (bad
                // syntax, missing "openapi" version field, etc.) — an expected "the user's file is
                // bad" outcome, not a server fault, so it's a 400 with the parser's own message
                // rather than an unhandled exception surfacing as a bare 500.
                return Results.BadRequest(new ImportSpecificationError(ex.Message));
            }
        })
        .WithName("ImportOpenApiSpecification")
        .Accepts<string>("text/plain");

        group.MapPost("/asyncapi", async (HttpRequest request, IMediator mediator, CancellationToken cancellationToken) =>
        {
            using var reader = new StreamReader(request.Body);
            var yamlContent = await reader.ReadToEndAsync(cancellationToken);

            var fileName = request.Headers.TryGetValue("X-File-Name", out var headerValue)
                ? headerValue.ToString()
                : "spec.yaml";

            try
            {
                var id = await mediator.Send(new ImportAsyncApiSpec(fileName, yamlContent), cancellationToken);
                return Results.Ok(new ImportSpecificationResult(id));
            }
            catch (InvalidOperationException ex)
            {
                // See the /openapi branch above — AsyncApiSpecificationParser throws the same way
                // for a file that doesn't parse.
                return Results.BadRequest(new ImportSpecificationError(ex.Message));
            }
        })
        .WithName("ImportAsyncApiSpecification")
        .Accepts<string>("text/plain");

        return app;
    }
}
