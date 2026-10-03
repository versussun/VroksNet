using Mediator;
using VroksNet.Application.Provisioning.ExportProvisioning;

namespace VroksNet.ApiService.Endpoints;

/// <summary>
/// Exporting the current configuration as a provisioning directory (ADR 0001): configure in the
/// UI, export, commit, mount at /app/provisioning. No logic lives here, per .claude/CLAUDE.md.
/// </summary>
public static class ProvisioningEndpoints
{
    public static IEndpointRouteBuilder MapProvisioningEndpoints(this IEndpointRouteBuilder app)
    {
        // A zip of specs/ and vroksnet.yaml. Connection values become valueFrom entries unless
        // inlineValues=true (only for a file that stays private: it then holds credentials).
        app.MapGet("/api/provisioning/export", async (bool? inlineValues, IMediator mediator, CancellationToken cancellationToken) =>
        {
            var zip = await mediator.Send(new ExportProvisioning(inlineValues ?? false), cancellationToken);
            return Results.File(zip, "application/zip", "vroksnet-provisioning.zip");
        })
        .WithName("ExportProvisioning");

        return app;
    }
}
