namespace VroksNet.Web;

public sealed record SpecificationDetails(
    Guid Id,
    string Title,
    SpecificationKind Kind,
    string RawContent,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    MockEndpointDetail[] Endpoints,
    string[] Protocols,
    string[] ConnectionTypes);
