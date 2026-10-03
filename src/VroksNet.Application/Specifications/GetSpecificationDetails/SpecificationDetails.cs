using VroksNet.Domain.ApiSpecifications;
using VroksNet.Domain.Connections;

namespace VroksNet.Application.Specifications.GetSpecificationDetails;

/// <summary>
/// Full detail view of one specification, including each endpoint's example template and the
/// original uploaded file (verbatim) for "view raw" in the UI. <see cref="Protocols"/> are its
/// AsyncAPI servers' protocols, and <see cref="ConnectionTypes"/> the connection types that speak
/// them — what the Test Scenario and Publisher forms offer first (empty: no preference).
/// </summary>
public sealed record SpecificationDetails(
    Guid Id,
    string Title,
    SpecificationKind Kind,
    string RawContent,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<MockEndpointDetail> Endpoints,
    IReadOnlyList<string> Protocols,
    IReadOnlyList<ConnectionServiceType> ConnectionTypes);
