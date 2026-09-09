using Mediator;
using VroksNet.Application.Connections.CreateConnection;
using VroksNet.Application.Connections.DeleteConnection;
using VroksNet.Application.Connections.ListConnections;
using VroksNet.Application.Connections.UpdateConnection;

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

        group.MapPost("/", async (CreateConnection request, IMediator mediator, CancellationToken cancellationToken) =>
        {
            var id = await mediator.Send(request, cancellationToken);
            return Results.Ok(new CreateConnectionResult(id));
        })
        .WithName("CreateConnection");

        group.MapPut("/{id:guid}", async (Guid id, UpdateConnection body, IMediator mediator, CancellationToken cancellationToken) =>
        {
            // The route's id is authoritative — the body's own Id (if any) is ignored.
            var found = await mediator.Send(body with { Id = id }, cancellationToken);
            return found ? Results.NoContent() : Results.NotFound();
        })
        .WithName("UpdateConnection");

        group.MapDelete("/{id:guid}", async (Guid id, IMediator mediator, CancellationToken cancellationToken) =>
        {
            var found = await mediator.Send(new DeleteConnection(id), cancellationToken);
            return found ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteConnection");

        return app;
    }
}
