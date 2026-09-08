using Mediator;
using VroksNet.Application.Mocking.InvokeMockEndpoint;

namespace VroksNet.ApiService.Endpoints;

/// <summary>
/// Serves the mocked endpoints themselves, under the "/mock" prefix — as opposed to
/// <see cref="SpecificationEndpoints"/>, which is the admin API for managing imported specs. A
/// request here is matched against imported specs' enabled operation keys and answered with the
/// spec's own static example (no placeholder substitution yet, no schema-based fallback — see
/// docs/project-brief.md section 2 "Динамика ответов"). Every handler just maps HTTP to a
/// Mediator request — no logic lives here, per .claude/CLAUDE.md.
/// </summary>
public static class MockInvocationEndpoints
{
    private static readonly string[] SupportedMethods = ["GET", "POST", "PUT", "PATCH", "DELETE"];

    public static IEndpointRouteBuilder MapMockInvocationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapMethods("/mock/{**path}", SupportedMethods, async (string? path, HttpContext context, IMediator mediator, CancellationToken cancellationToken) =>
        {
            var result = await mediator.Send(new InvokeMockEndpoint(context.Request.Method, "/" + (path ?? string.Empty)), cancellationToken);

            if (!result.Matched)
            {
                return Results.Problem(
                    detail: $"No enabled mock endpoint matches {context.Request.Method} /{path}.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            // No example in the spec for this (matched) operation — a static "{}" stands in until
            // schema-based generation exists.
            return Results.Content(result.ExampleJson ?? "{}", "application/json");
        })
        .WithName("InvokeMockEndpoint");

        return app;
    }
}
