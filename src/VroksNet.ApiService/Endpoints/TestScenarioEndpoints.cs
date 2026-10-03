using Mediator;
using VroksNet.Application.TestRuns.StartTestRun;
using VroksNet.Application.TestScenarios.CreateTestScenario;
using VroksNet.Application.TestScenarios.DeleteTestScenario;
using VroksNet.Application.TestScenarios.GetTestScenario;
using VroksNet.Application.TestScenarios.ListTestScenarios;
using VroksNet.Application.TestScenarios.PreviewSchedule;
using VroksNet.Application.TestScenarios.RunTestScenario;
using VroksNet.Application.TestScenarios.UpdateTestScenario;

using VroksNet.ApiService.Endpoints.Responses;

namespace VroksNet.ApiService.Endpoints;

/// <summary>
/// Admin CRUD over saved TestScenarios (a message from a specification's operation, sent through
/// a connection) plus the "run it now" action. Every handler just maps HTTP to a Mediator
/// request — no logic lives here, per .claude/CLAUDE.md.
/// </summary>
public static class TestScenarioEndpoints
{
    public static IEndpointRouteBuilder MapTestScenarioEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/test-scenarios");

        group.MapGet("/", async (IMediator mediator, CancellationToken cancellationToken) =>
        {
            var scenarios = await mediator.Send(new ListTestScenarios(), cancellationToken);
            return Results.Ok(scenarios);
        })
        .WithName("ListTestScenarios");

        // The next run times of a cron schedule, or why it's invalid (ADR 0002) — the form calls it
        // as the user types. Always 200: an invalid schedule is an answer here, not a bad request.
        group.MapGet("/schedule-preview", async (string? schedule, string? timeZone, int? count, IMediator mediator, CancellationToken cancellationToken) =>
        {
            var preview = await mediator.Send(new PreviewSchedule(schedule, timeZone, count ?? 5), cancellationToken);
            return Results.Ok(preview);
        })
        .WithName("PreviewSchedule");

        // Also carries the scenario's last-run status (LastRunAt/LastRunSuccess/LastRunMessage)
        // — lets a caller poll one scenario's status without listing all of them.
        group.MapGet("/{id:guid}", async (Guid id, IMediator mediator, CancellationToken cancellationToken) =>
        {
            var scenario = await mediator.Send(new GetTestScenario(id), cancellationToken);
            return scenario is not null ? Results.Ok(scenario) : Results.NotFound();
        })
        .WithName("GetTestScenario");

        // Invalid input (the handler's ArgumentException) — a blank or taken name, an incompatible
        // operation/connection pair — is a 400 with the reason.
        group.MapPost("/", async (CreateTestScenario request, IMediator mediator, CancellationToken cancellationToken) =>
        {
            try
            {
                var id = await mediator.Send(request, cancellationToken);
                return Results.Ok(new CreateTestScenarioResult(id));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex);
            }
        })
        .WithName("CreateTestScenario");

        group.MapPut("/{id:guid}", async (Guid id, UpdateTestScenario body, IMediator mediator, CancellationToken cancellationToken) =>
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
        .WithName("UpdateTestScenario");

        group.MapDelete("/{id:guid}", async (Guid id, IMediator mediator, CancellationToken cancellationToken) =>
        {
            var found = await mediator.Send(new DeleteTestScenario(id), cancellationToken);
            return found ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteTestScenario");

        group.MapPost("/{id:guid}/run", async (Guid id, IMediator mediator, CancellationToken cancellationToken) =>
        {
            var result = await mediator.Send(new RunTestScenario(id), cancellationToken);
            return result is not null ? Results.Ok(result) : Results.NotFound();
        })
        .WithName("RunTestScenario");

        // A background run (ADR 0002): queued and picked up by the worker within a second. Poll
        // GET /api/test-runs/{runId} for its status and result.
        group.MapPost("/{id:guid}/runs", async (Guid id, IMediator mediator, CancellationToken cancellationToken) =>
        {
            var runId = await mediator.Send(new StartTestRun(id), cancellationToken);
            return runId is { } started
                ? Results.Accepted($"/api/test-runs/{started}", new StartTestRunResult(started))
                : Results.NotFound();
        })
        .WithName("StartTestRun");

        return app;
    }

    private static IResult BadRequest(ArgumentException ex) => Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
}
