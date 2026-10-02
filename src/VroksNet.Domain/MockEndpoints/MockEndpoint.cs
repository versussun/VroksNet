using System.Globalization;

namespace VroksNet.Domain.MockEndpoints;

/// <summary>
/// One mockable operation parsed out of an <see cref="ApiSpecifications.ApiSpecification"/> —
/// an OpenAPI method+path, or an AsyncAPI channel+action: what was found in the spec (its example
/// template, status and schemas) and how the mock serves it.
/// </summary>
public sealed class MockEndpoint
{
    public Guid Id { get; set; }

    public Guid SpecificationId { get; set; }

    /// <summary>E.g. "GET /pets/{id}" for OpenAPI, or "orders.created:send" for AsyncAPI.</summary>
    public string OperationKey { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Provider mode ("Тип 3" in docs/contract-testing-plan.md): also answer this operation at its
    /// real path on the separate provider port, not just under /mock. Only for HTTP operations, and
    /// only when no other provider-mode operation could match the same request — see
    /// <see cref="OperationOverlap"/>.
    /// </summary>
    public bool ServeAtRealPath { get; set; }

    /// <summary>
    /// The example (from the spec, or a generated placeholder) used as the response/payload template —
    /// its <c>{{request.*}}</c>/<c>{{uuid}}</c>/<c>{{now}}</c> placeholders are filled in per call.
    /// </summary>
    public string? ExampleTemplate { get; set; }

    /// <summary>
    /// The status the mock answers with (OpenAPI only): the status of the response the example was
    /// taken from, or the lowest declared 2xx when there's none. Null for AsyncAPI operations, for
    /// operations that declare no 2xx and no example, and for operations imported before this was
    /// tracked — the mock answers those with 200.
    /// </summary>
    public int? ExampleStatusCode { get; set; }

    /// <summary>The operation's request-body JSON Schema (OpenAPI only), self-contained (local $refs already inlined). Null if there's no request body, or for AsyncAPI operations.</summary>
    public string? RequestSchema { get; set; }

    /// <summary>The operation's response (OpenAPI) or message payload (AsyncAPI) JSON Schema, self-contained the same way.</summary>
    public string? ResponseSchema { get; set; }

    /// <summary>
    /// Every response the OpenAPI operation declares, keyed by its OpenAPI status key ("200",
    /// "4XX", "default" — range keys normalized to upper case), each mapped to that response's
    /// self-contained JSON body schema, or null if it declares no JSON body. Empty for AsyncAPI
    /// operations, and for OpenAPI operations imported before this was tracked — which reads as
    /// "nothing to validate against", not "no status is allowed".
    /// </summary>
    public Dictionary<string, string?> ResponseSchemasByStatus { get; set; } = [];

    /// <summary>
    /// Finds the response the spec declares for <paramref name="statusCode"/>, using OpenAPI's
    /// precedence: the exact code, then its range ("2XX"), then "default". Returns false if the
    /// spec declares nothing that covers it; <paramref name="schemaJson"/> is null when the
    /// matching response declares no JSON body.
    /// </summary>
    public bool TryGetDeclaredResponse(int statusCode, out string? schemaJson)
    {
        string[] candidates = [statusCode.ToString(CultureInfo.InvariantCulture), $"{statusCode / 100}XX", "default"];
        foreach (var key in candidates)
        {
            if (ResponseSchemasByStatus.TryGetValue(key, out schemaJson))
            {
                return true;
            }
        }

        schemaJson = null;
        return false;
    }
}
