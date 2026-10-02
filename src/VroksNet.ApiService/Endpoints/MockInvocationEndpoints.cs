using Mediator;
using VroksNet.Application.CallRecords;
using VroksNet.Application.Mocking.InvokeMockEndpoint;

namespace VroksNet.ApiService.Endpoints;

/// <summary>
/// Serves the mocked endpoints themselves, under the "/mock" prefix — as opposed to
/// <see cref="SpecificationEndpoints"/>, which is the admin API for managing imported specs. A
/// request here is matched against imported specs' enabled operation keys and answered with the
/// spec's own static example (no placeholder substitution yet, no schema-based fallback — see
/// docs/project-brief.md section 2 "Динамика ответов"). Every call is logged to the call history
/// by InvokeMockEndpointHandler. Every handler just maps HTTP to a
/// Mediator request — no logic lives here, per .claude/CLAUDE.md.
/// </summary>
public static class MockInvocationEndpoints
{
    private static readonly string[] SupportedMethods = ["GET", "POST", "PUT", "PATCH", "DELETE"];

    public static IEndpointRouteBuilder MapMockInvocationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapMethods("/mock/{**path}", SupportedMethods, async (string? path, HttpContext context, IMediator mediator, CancellationToken cancellationToken) =>
        {
            // The body only feeds the call history, which keeps at most CallRecordSnapshot.MaxLength
            // characters — so don't buffer more than that (+1, to know it was longer).
            using var reader = new StreamReader(context.Request.Body);
            var buffer = new char[CallRecordSnapshot.MaxLength + 1];
            var length = await reader.ReadBlockAsync(buffer.AsMemory(), cancellationToken);
            var body = new string(buffer, 0, length);

            var result = await mediator.Send(
                new InvokeMockEndpoint(context.Request.Method, "/" + (path ?? string.Empty), context.Request.QueryString.Value ?? string.Empty, body.Length > 0 ? body : null),
                cancellationToken);

            return result.Matched
                ? Results.Content(result.ResponseBody, "application/json")
                : Results.Problem(detail: result.ResponseBody, statusCode: StatusCodes.Status404NotFound);
        })
        .WithName("InvokeMockEndpoint");

        return app;
    }
}
