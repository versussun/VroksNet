using Mediator;
using VroksNet.Application.TestScenarios.CreateTestScenario;
using VroksNet.Application.TestScenarios.DeleteTestScenario;
using VroksNet.Application.TestScenarios.GetTestScenario;
using VroksNet.Application.TestScenarios.ListTestScenarios;
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

        // Also carries the scenario's last-run status (LastRunAt/LastRunSuccess/LastRunMessage)
        // — lets a caller poll one scenario's status without listing all of them.
        group.MapGet("/{id:guid}", async (Guid id, IMediator mediator, CancellationToken cancellationToken) =>
        {
            var scenario = await mediator.Send(new GetTestScenario(id), cancellationToken);
            return scenario is not null ? Results.Ok(scenario) : Results.NotFound();
        })
        .WithName("GetTestScenario");

        group.MapPost("/", async (CreateTestScenario request, IMediator mediator, CancellationToken cancellationToken) =>
        {
            var id = await mediator.Send(request, cancellationToken);
            return Results.Ok(new CreateTestScenarioResult(id));
        })
        .WithName("CreateTestScenario");

        group.MapPut("/{id:guid}", async (Guid id, UpdateTestScenario body, IMediator mediator, CancellationToken cancellationToken) =>
        {
            // The route's id is authoritative — the body's own Id (if any) is ignored.
            var found = await mediator.Send(body with { Id = id }, cancellationToken);
            return found ? Results.NoContent() : Results.NotFound();
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

        return app;
    }
}
