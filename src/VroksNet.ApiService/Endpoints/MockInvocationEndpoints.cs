using System.Text;
using Mediator;
using Microsoft.Net.Http.Headers;
using VroksNet.Application.CallRecords;
using VroksNet.Application.Mocking.InvokeMockEndpoint;

namespace VroksNet.ApiService.Endpoints;

/// <summary>
/// Serves the mocked endpoints themselves, under the "/mock" prefix — as opposed to
/// <see cref="SpecificationEndpoints"/>, which is the admin API for managing imported specs. A
/// request here is matched against imported specs' enabled operation keys and answered with the
/// spec's example, its placeholders filled in from the request, at the spec's status (see
/// docs/contract-testing-plan.md 4.6; no schema-based fallback for a missing example). Every call is logged to the call history
/// by InvokeMockEndpointHandler. The provider port (<see cref="ProviderPortSetup"/>) answers at
/// real paths through the same <see cref="InvokeAsync"/>. Every handler just maps HTTP to a
/// Mediator request — no logic lives here, per .claude/CLAUDE.md.
/// </summary>
public static class MockInvocationEndpoints
{
    private static readonly string[] SupportedMethods = ["GET", "POST", "PUT", "PATCH", "DELETE"];

    public static IEndpointRouteBuilder MapMockInvocationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapMethods("/mock/{**path}", SupportedMethods, (string? path, HttpContext context, IMediator mediator, CancellationToken cancellationToken)
                => InvokeAsync(context, mediator, "/" + (path ?? string.Empty), providerMode: false, cancellationToken))
            .WithName("InvokeMockEndpoint");

        return app;
    }

    /// <summary>Maps one HTTP request to <see cref="InvokeMockEndpoint"/> and its result back to a response — shared by "/mock" and the provider port.</summary>
    internal static async Task<IResult> InvokeAsync(HttpContext context, IMediator mediator, string path, bool providerMode, CancellationToken cancellationToken)
    {
        // The body only feeds validation, {{request.body.*}} and the call history, which keeps at most
        // CallRecordSnapshot.MaxLength characters — so don't buffer more than that (+1, to know it
        // was longer). Decoded with the request's own charset, if it names one.
        var contentType = context.Request.ContentType;
        var encoding = MediaTypeHeaderValue.TryParse(contentType, out var mediaType) ? mediaType.Encoding ?? Encoding.UTF8 : Encoding.UTF8;
        using var reader = new StreamReader(context.Request.Body, encoding);
        var buffer = new char[CallRecordSnapshot.MaxLength + 1];
        var length = await reader.ReadBlockAsync(buffer.AsMemory(), cancellationToken);
        var body = new string(buffer, 0, length);

        var query = context.Request.Query.ToDictionary(pair => pair.Key, pair => pair.Value.ToString(), StringComparer.Ordinal);
        var headers = context.Request.Headers.ToDictionary(pair => pair.Key, pair => pair.Value.ToString(), StringComparer.OrdinalIgnoreCase);

        var result = await mediator.Send(
            new InvokeMockEndpoint(context.Request.Method, path, context.Request.QueryString.Value ?? string.Empty, body.Length > 0 ? body : null, providerMode, contentType, query, headers),
            cancellationToken);

        if (!result.Matched)
        {
            return Results.Problem(detail: result.ResponseBody, statusCode: result.StatusCode);
        }

        // An empty body is a status that can't carry one (204, 304) — writing even "" with a content type would be refused.
        return result.ResponseBody.Length == 0
            ? Results.StatusCode(result.StatusCode)
            : Results.Content(result.ResponseBody, "application/json", statusCode: result.StatusCode);
    }
}
