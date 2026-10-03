using Mediator;
using VroksNet.Application.System.GetProviderInfo;
using VroksNet.Application.System.GetStorageStatus;
using VroksNet.Application.System.GetSystemInfo;
using VroksNet.Application.System.ListConnectionTypes;

namespace VroksNet.ApiService.Endpoints;

/// <summary>
/// Read-only system/environment info for the Admin UI and for tooling (the Aspire hosting package):
/// storage mode, provider mode, version + provisioning state, and the connection types. No logic lives here, per .claude/CLAUDE.md.
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

        // Whether provider mode's port is configured, and where a service under test should point.
        app.MapGet("/api/system/provider", async (IMediator mediator, CancellationToken cancellationToken) =>
        {
            var info = await mediator.Send(new GetProviderInfo(), cancellationToken);
            return Results.Ok(info);
        })
        .WithName("GetProviderInfo");

        // Version, container-contract version and provisioning's report (docs/container-contract.md §6).
        app.MapGet("/api/system/info", async (IMediator mediator, CancellationToken cancellationToken) =>
        {
            var info = await mediator.Send(new GetSystemInfo(), cancellationToken);
            return Results.Ok(info);
        })
        .WithName("GetSystemInfo");

        // Every connection type, what its value looks like and what it can do (ADR 0003) — the
        // Admin UI builds its type list and connection filters from this.
        app.MapGet("/api/system/connection-types", async (IMediator mediator, CancellationToken cancellationToken) =>
        {
            var types = await mediator.Send(new ListConnectionTypes(), cancellationToken);
            return Results.Ok(types);
        })
        .WithName("ListConnectionTypes");

        return app;
    }
}
