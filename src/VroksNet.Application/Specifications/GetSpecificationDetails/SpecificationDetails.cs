using VroksNet.Domain.ApiSpecifications;

namespace VroksNet.Application.Specifications.GetSpecificationDetails;

/// <summary>Full detail view of one specification, including each endpoint's example template and the original uploaded file (verbatim) for "view raw" in the UI.</summary>
public sealed record SpecificationDetails(
    Guid Id,
    string Title,
    SpecificationKind Kind,
    string RawContent,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<MockEndpointDetail> Endpoints);
