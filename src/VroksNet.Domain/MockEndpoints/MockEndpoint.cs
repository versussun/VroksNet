namespace VroksNet.Domain.MockEndpoints;

/// <summary>
/// One mockable operation parsed out of an <see cref="ApiSpecifications.ApiSpecification"/> —
/// an OpenAPI method+path, or an AsyncAPI channel+action. The full request-matching/response
/// generation engine is Phase 01 work (see docs/project-brief.md); for now this just tracks
/// what was found in the spec and whether it's turned on.
/// </summary>
public sealed class MockEndpoint
{
    public Guid Id { get; set; }

    public Guid SpecificationId { get; set; }

    /// <summary>E.g. "GET /pets/{id}" for OpenAPI, or "orders.created:send" for AsyncAPI.</summary>
    public string OperationKey { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    /// <summary>The example (from the spec, or a generated placeholder) used as the response/payload template.</summary>
    public string? ExampleTemplate { get; set; }
}
