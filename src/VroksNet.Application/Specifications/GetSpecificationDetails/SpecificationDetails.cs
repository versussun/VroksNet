using VroksNet.Domain.ApiSpecifications;

namespace VroksNet.Application.Specifications.GetSpecificationDetails;

/// <summary>Full detail view of one specification, including each endpoint's example template.</summary>
public sealed record SpecificationDetails(
    Guid Id,
    string Title,
    SpecificationKind Kind,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<MockEndpointDetail> Endpoints);
