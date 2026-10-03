using Mediator;
using VroksNet.Application.Connections.CreateConnection;
using VroksNet.Application.Connections.DeleteConnection;
using VroksNet.Application.Connections.ListConnections;
using VroksNet.Application.Connections.TestConnection;
using VroksNet.Application.Connections.TestConnectionValue;
using VroksNet.Application.Connections.UpdateConnection;

using VroksNet.ApiService.Endpoints.Responses;

namespace VroksNet.ApiService.Endpoints;

/// <summary>
/// Admin CRUD over the Settings page's Connections section — named connections (a URL or a
/// connection string, depending on service type) to systems later features will use. Every
/// handler just maps HTTP to a Mediator request — no logic lives here, per .claude/CLAUDE.md.
/// </summary>
public static class ConnectionEndpoints
{
    public static IEndpointRouteBuilder MapConnectionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/connections");

        group.MapGet("/", async (IMediator mediator, CancellationToken cancellationToken) =>
        {
            var connections = await mediator.Send(new ListConnections(), cancellationToken);
            return Results.Ok(connections);
        })
        .WithName("ListConnections");

        // Invalid input (the handler's ArgumentException) — a blank or taken name, an incompatible
        // operation/connection pair — is a 400 with the reason.
        group.MapPost("/", async (CreateConnection request, IMediator mediator, CancellationToken cancellationToken) =>
        {
            try
            {
                var id = await mediator.Send(request, cancellationToken);
                return Results.Ok(new CreateConnectionResult(id));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex);
            }
        })
        .WithName("CreateConnection");

        group.MapPut("/{id:guid}", async (Guid id, UpdateConnection body, IMediator mediator, CancellationToken cancellationToken) =>
        {
            try
            {
                // The route's id is authoritative — the body's own Id (if any) is ignored.
                var found = await mediator.Send(body with { Id = id }, cancellationToken);
                return found ? Results.NoContent() : Results.NotFound();
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex);
            }
        })
        .WithName("UpdateConnection");

        group.MapDelete("/{id:guid}", async (Guid id, IMediator mediator, CancellationToken cancellationToken) =>
        {
            var found = await mediator.Send(new DeleteConnection(id), cancellationToken);
            return found ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteConnection");

        group.MapPost("/{id:guid}/test", async (Guid id, IMediator mediator, CancellationToken cancellationToken) =>
        {
            var result = await mediator.Send(new TestConnection(id), cancellationToken);
            return result is not null ? Results.Ok(result) : Results.NotFound();
        })
        .WithName("TestConnection");

        // Tests a URL/connection string the Add/Edit form hasn't saved yet — the route reads
        // "/test" (no {id}) rather than "/{id:guid}/test" above, so routing tells the two apart
        // on shape alone.
        group.MapPost("/test", async (TestConnectionValue request, IMediator mediator, CancellationToken cancellationToken) =>
        {
            var result = await mediator.Send(request, cancellationToken);
            return Results.Ok(result);
        })
        .WithName("TestConnectionValue");

        return app;
    }

    private static IResult BadRequest(ArgumentException ex) => Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
}
