using Mediator;
using VroksNet.Application.CallRecords.ClearCallRecords;
using VroksNet.Application.CallRecords.GetCallRecord;
using VroksNet.Application.CallRecords.ListCallRecords;

using VroksNet.ApiService.Endpoints.Responses;

namespace VroksNet.ApiService.Endpoints;

/// <summary>
/// The call history: every Test Scenario run and every inbound mock call, newest first. Every
/// handler just maps HTTP to a Mediator request — no logic lives here, per .claude/CLAUDE.md.
/// </summary>
public static class CallRecordEndpoints
{
    public static IEndpointRouteBuilder MapCallRecordEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/call-records");

        // Query: specificationId, mockEndpointId, testScenarioId, direction (e.g. "InboundHttpRequest"),
        // contractValid, cursor (the previous page's nextCursor), limit.
        group.MapGet("/", async ([AsParameters] ListCallRecords request, IMediator mediator, CancellationToken cancellationToken) =>
        {
            var page = await mediator.Send(request, cancellationToken);
            return Results.Ok(page);
        })
        .WithName("ListCallRecords");

        // The request/response bodies the list leaves out, for one record.
        group.MapGet("/{id:guid}", async (Guid id, IMediator mediator, CancellationToken cancellationToken) =>
        {
            var details = await mediator.Send(new GetCallRecord(id), cancellationToken);
            return details is not null ? Results.Ok(details) : Results.NotFound();
        })
        .WithName("GetCallRecord");

        group.MapDelete("/", async (IMediator mediator, CancellationToken cancellationToken) =>
        {
            var deleted = await mediator.Send(new ClearCallRecords(), cancellationToken);
            return Results.Ok(new ClearCallRecordsResult(deleted));
        })
        .WithName("ClearCallRecords");

        return app;
    }
}
