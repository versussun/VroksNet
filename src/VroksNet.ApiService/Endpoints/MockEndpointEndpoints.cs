using Mediator;
using VroksNet.ApiService.Endpoints.Requests;
using VroksNet.Application.Mocking.SetEndpointEnabled;
using VroksNet.Application.Mocking.SetEndpointProviderMode;

namespace VroksNet.ApiService.Endpoints;

/// <summary>
/// Admin actions on a single imported operation. Every handler just maps HTTP to a Mediator
/// request — no logic lives here, per .claude/CLAUDE.md.
/// </summary>
public static class MockEndpointEndpoints
{
    public static IEndpointRouteBuilder MapMockEndpointEndpoints(this IEndpointRouteBuilder app)
    {
        // Body: { "enabled": true|false }. 409 with the reason when turning it on is refused
        // (a non-HTTP operation, or an overlap with an operation already served at its real path).
        app.MapPut("/api/mock-endpoints/{id:guid}/provider-mode", async (Guid id, ProviderModeBody body, IMediator mediator, CancellationToken cancellationToken) =>
        {
            if (body.Enabled is not { } enabled)
            {
                return Results.Problem(detail: "The body must say { \"enabled\": true } or { \"enabled\": false }.", statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await mediator.Send(new SetEndpointProviderMode(id, enabled), cancellationToken);
            return result switch
            {
                { Found: false } => Results.NotFound(),
                { Refusal: { } refusal } => Results.Problem(detail: refusal, statusCode: StatusCodes.Status409Conflict),
                _ => Results.NoContent()
            };
        })
        .WithName("SetEndpointProviderMode");

        // Body: { "enabled": true|false }. 409 with the reason for a non-HTTP operation.
        app.MapPut("/api/mock-endpoints/{id:guid}/enabled", async (Guid id, EndpointEnabledBody body, IMediator mediator, CancellationToken cancellationToken) =>
        {
            if (body.Enabled is not { } enabled)
            {
                return Results.Problem(detail: "The body must say { \"enabled\": true } or { \"enabled\": false }.", statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await mediator.Send(new SetEndpointEnabled(id, enabled), cancellationToken);
            return result switch
            {
                { Found: false } => Results.NotFound(),
                { Refusal: { } refusal } => Results.Problem(detail: refusal, statusCode: StatusCodes.Status409Conflict),
                _ => Results.NoContent()
            };
        })
        .WithName("SetEndpointEnabled");

        return app;
    }
}
