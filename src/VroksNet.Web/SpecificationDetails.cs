namespace VroksNet.Web;

public sealed record SpecificationDetails(
    Guid Id,
    string Title,
    SpecificationKind Kind,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    MockEndpointDetail[] Endpoints);
