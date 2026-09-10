using Mediator;
using VroksNet.Application.System.GetStorageStatus;

namespace VroksNet.ApiService.Endpoints;

/// <summary>
/// Read-only system/environment info for the Admin UI — currently just the storage mode (see
/// IStorageStatusProvider). No logic lives here, per .claude/CLAUDE.md.
/// </summary>
public static class SystemEndpoints
{
    public static IEndpointRouteBuilder MapSystemEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/system/storage", async (IMediator mediator, CancellationToken cancellationToken) =>
        {
            var status = await mediator.Send(new GetStorageStatus(), cancellationToken);
            return Results.Ok(status);
        })
        .WithName("GetStorageStatus");

        return app;
    }
}
