using Mediator;
using VroksNet.ApiService.Endpoints.Responses;
using VroksNet.Application.TestRuns.CancelTestRun;
using VroksNet.Application.TestSuites.CancelSuiteRun;
using VroksNet.Application.TestSuites.CreateTestSuite;
using VroksNet.Application.TestSuites.DeleteTestSuite;
using VroksNet.Application.TestSuites.GetSuiteRun;
using VroksNet.Application.TestSuites.GetTestSuite;
using VroksNet.Application.TestSuites.ListSuiteRuns;
using VroksNet.Application.TestSuites.ListTestSuites;
using VroksNet.Application.TestSuites.StartSuiteRun;
using VroksNet.Application.TestSuites.UpdateTestSuite;

namespace VroksNet.ApiService.Endpoints;

/// <summary>
/// Test suites (ADR 0002, "Suites"): CRUD, starting a run, and the run history a CI pipeline polls.
/// <c>{suite}</c> is the suite's id or its name, so a pipeline can use the name it was provisioned
/// with. Every handler just maps HTTP to a Mediator request — no logic lives here, per .claude/CLAUDE.md.
/// </summary>
public static class TestSuiteEndpoints
{
    public static IEndpointRouteBuilder MapTestSuiteEndpoints(this IEndpointRouteBuilder app)
    {
        var suites = app.MapGroup("/api/test-suites");

        suites.MapGet("/", async (IMediator mediator, CancellationToken cancellationToken) =>
            Results.Ok(await mediator.Send(new ListTestSuites(), cancellationToken)))
        .WithName("ListTestSuites");

        suites.MapGet("/{suite}", async (string suite, IMediator mediator, CancellationToken cancellationToken) =>
        {
            var found = await mediator.Send(new GetTestSuite(suite), cancellationToken);
            return found is not null ? Results.Ok(found) : Results.NotFound();
        })
        .WithName("GetTestSuite");

        // A blank or taken name, no scenarios, a repeat or an unknown scenario is a 400 with the reason.
        suites.MapPost("/", async (CreateTestSuite request, IMediator mediator, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(new CreateTestSuiteResult(await mediator.Send(request, cancellationToken)));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex);
            }
        })
        .WithName("CreateTestSuite");

        suites.MapPut("/{id:guid}", async (Guid id, UpdateTestSuite body, IMediator mediator, CancellationToken cancellationToken) =>
        {
            try
            {
                return await mediator.Send(body with { Id = id }, cancellationToken) ? Results.NoContent() : Results.NotFound();
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex);
            }
        })
        .WithName("UpdateTestSuite");

        suites.MapDelete("/{id:guid}", async (Guid id, IMediator mediator, CancellationToken cancellationToken) =>
            await mediator.Send(new DeleteTestSuite(id), cancellationToken) ? Results.NoContent() : Results.NotFound())
        .WithName("DeleteTestSuite");

        // Queued and started by the worker within a second; poll GET /api/suite-runs/{suiteRunId}.
        suites.MapPost("/{suite}/runs", async (string suite, IMediator mediator, CancellationToken cancellationToken) =>
        {
            var suiteRunId = await mediator.Send(new StartSuiteRun(suite), cancellationToken);
            return suiteRunId is { } started
                ? Results.Accepted($"/api/suite-runs/{started}", new StartSuiteRunResult(started))
                : Results.NotFound();
        })
        .WithName("StartSuiteRun");

        suites.MapGet("/{suite}/runs", async (string suite, int? limit, IMediator mediator, CancellationToken cancellationToken) =>
        {
            var runs = await mediator.Send(new ListSuiteRuns(suite, limit ?? 20), cancellationToken);
            return runs is not null ? Results.Ok(runs) : Results.NotFound();
        })
        .WithName("ListSuiteRuns");

        // What a pipeline polls: { status, failed: [...] } and the rest of the latest run.
        suites.MapGet("/{suite}/runs/latest", async (string suite, IMediator mediator, CancellationToken cancellationToken) =>
        {
            var runs = await mediator.Send(new ListSuiteRuns(suite, 1), cancellationToken);
            return runs switch
            {
                null => Results.Problem(detail: $"No test suite \"{suite}\".", statusCode: StatusCodes.Status404NotFound),
                [] => Results.Problem(detail: $"Test suite \"{suite}\" hasn't been run yet.", statusCode: StatusCodes.Status404NotFound),
                [var latest, ..] => Results.Ok(latest)
            };
        })
        .WithName("GetLatestSuiteRun");

        var suiteRuns = app.MapGroup("/api/suite-runs");

        suiteRuns.MapGet("/{id:guid}", async (Guid id, IMediator mediator, CancellationToken cancellationToken) =>
        {
            var run = await mediator.Send(new GetSuiteRun(id), cancellationToken);
            return run is not null ? Results.Ok(run) : Results.NotFound();
        })
        .WithName("GetSuiteRun");

        // 204 when cancelled (a running suite run ends as Cancelled within moments); 409 when it had already finished.
        suiteRuns.MapPost("/{id:guid}/cancel", async (Guid id, IMediator mediator, CancellationToken cancellationToken) =>
            await mediator.Send(new CancelSuiteRun(id), cancellationToken) switch
            {
                CancelTestRunResult.Cancelled => Results.NoContent(),
                CancelTestRunResult.NotFound => Results.NotFound(),
                _ => Results.Problem(detail: "The suite run has already finished.", statusCode: StatusCodes.Status409Conflict)
            })
        .WithName("CancelSuiteRun");

        return app;
    }

    private static IResult BadRequest(ArgumentException ex) => Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
}
