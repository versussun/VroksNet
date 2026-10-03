using Mediator;
using VroksNet.Application.System.GetProviderInfo;
using VroksNet.Application.System.GetStorageStatus;
using VroksNet.Application.System.GetSystemInfo;

namespace VroksNet.ApiService.Endpoints;

/// <summary>
/// Read-only system/environment info for the Admin UI and for tooling (the Aspire hosting package):
/// storage mode, provider mode, and version + provisioning state. No logic lives here, per .claude/CLAUDE.md.
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

        return app;
    }
}
