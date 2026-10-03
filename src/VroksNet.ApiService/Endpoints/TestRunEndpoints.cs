using Mediator;
using VroksNet.Application.TestRuns.CancelTestRun;
using VroksNet.Application.TestRuns.GetTestRun;
using VroksNet.Application.TestRuns.ListTestRuns;

namespace VroksNet.ApiService.Endpoints;

/// <summary>
/// Test-run history (ADR 0002): every run of a test scenario, synchronous or in the background,
/// newest first; one run's status and result; and cancelling a queued or running one. Every
/// handler just maps HTTP to a Mediator request — no logic lives here, per .claude/CLAUDE.md.
/// </summary>
public static class TestRunEndpoints
{
    public static IEndpointRouteBuilder MapTestRunEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/test-runs");

        // Query: testScenarioId, status (e.g. "Running"), cursor (the previous page's nextCursor), limit.
        group.MapGet("/", async ([AsParameters] ListTestRuns request, IMediator mediator, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await mediator.Send(request, cancellationToken));
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        })
        .WithName("ListTestRuns");

        group.MapGet("/{id:guid}", async (Guid id, IMediator mediator, CancellationToken cancellationToken) =>
        {
            var run = await mediator.Send(new GetTestRun(id), cancellationToken);
            return run is not null ? Results.Ok(run) : Results.NotFound();
        })
        .WithName("GetTestRun");

        // 204 when cancelled — a running run then ends as Cancelled within moments, so poll it;
        // 409 when it had already finished.
        group.MapPost("/{id:guid}/cancel", async (Guid id, IMediator mediator, CancellationToken cancellationToken) =>
        {
            return await mediator.Send(new CancelTestRun(id), cancellationToken) switch
            {
                CancelTestRunResult.Cancelled => Results.NoContent(),
                CancelTestRunResult.NotFound => Results.NotFound(),
                _ => Results.Problem(detail: "The run has already finished.", statusCode: StatusCodes.Status409Conflict)
            };
        })
        .WithName("CancelTestRun");

        return app;
    }
}
