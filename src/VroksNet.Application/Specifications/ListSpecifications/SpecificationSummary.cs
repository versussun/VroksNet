using VroksNet.Domain.ApiSpecifications;

namespace VroksNet.Application.Specifications.ListSpecifications;

/// <summary>A lightweight projection for list views — no <see cref="ApiSpecification.RawContent"/>.</summary>
public sealed record SpecificationSummary(Guid Id, string Title, SpecificationKind Kind, int EndpointCount, DateTimeOffset UpdatedAt);
