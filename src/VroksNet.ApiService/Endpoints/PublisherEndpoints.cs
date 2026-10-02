using Mediator;
using VroksNet.ApiService.Endpoints.Requests;
using VroksNet.ApiService.Endpoints.Responses;
using VroksNet.Application.Publishers.CreatePublisher;
using VroksNet.Application.Publishers.DeletePublisher;
using VroksNet.Application.Publishers.ListPublishers;
using VroksNet.Application.Publishers.PublishNow;
using VroksNet.Application.Publishers.SetPublisherEnabled;
using VroksNet.Application.Publishers.UpdatePublisher;

namespace VroksNet.ApiService.Endpoints;

/// <summary>
/// Admin CRUD over Publishers (async mocks: an AsyncAPI operation published through a broker
/// connection on a schedule), plus start/stop and "publish now". Invalid input (the handlers'
/// <see cref="ArgumentException"/>) is a 400 with the reason. Every handler just maps HTTP to a
/// Mediator request — no logic lives here, per .claude/CLAUDE.md.
/// </summary>
public static class PublisherEndpoints
{
    public static IEndpointRouteBuilder MapPublisherEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/publishers");

        group.MapGet("/", async (IMediator mediator, CancellationToken cancellationToken) =>
        {
            var publishers = await mediator.Send(new ListPublishers(), cancellationToken);
            return Results.Ok(publishers);
        })
        .WithName("ListPublishers");

        group.MapPost("/", async (CreatePublisher request, IMediator mediator, CancellationToken cancellationToken) =>
        {
            try
            {
                var id = await mediator.Send(request, cancellationToken);
                return Results.Ok(new CreatePublisherResult(id));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex);
            }
        })
        .WithName("CreatePublisher");

        group.MapPut("/{id:guid}", async (Guid id, UpdatePublisher body, IMediator mediator, CancellationToken cancellationToken) =>
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
        .WithName("UpdatePublisher");

        group.MapDelete("/{id:guid}", async (Guid id, IMediator mediator, CancellationToken cancellationToken) =>
        {
            var found = await mediator.Send(new DeletePublisher(id), cancellationToken);
            return found ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeletePublisher");

        // Body: { "enabled": true|false } — start or stop publishing on the schedule.
        group.MapPut("/{id:guid}/enabled", async (Guid id, PublisherEnabledBody body, IMediator mediator, CancellationToken cancellationToken) =>
        {
            if (body.Enabled is not { } enabled)
            {
                return Results.Problem(detail: "The body must say { \"enabled\": true } or { \"enabled\": false }.", statusCode: StatusCodes.Status400BadRequest);
            }

            var found = await mediator.Send(new SetPublisherEnabled(id, enabled), cancellationToken);
            return found ? Results.NoContent() : Results.NotFound();
        })
        .WithName("SetPublisherEnabled");

        group.MapPost("/{id:guid}/publish", async (Guid id, IMediator mediator, CancellationToken cancellationToken) =>
        {
            var result = await mediator.Send(new PublishNow(id), cancellationToken);
            return result is not null ? Results.Ok(result) : Results.NotFound();
        })
        .WithName("PublishNow");

        return app;
    }

    private static IResult BadRequest(ArgumentException ex) => Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
}
