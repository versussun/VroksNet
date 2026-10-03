namespace VroksNet.Web;

public sealed record SpecificationSummary(Guid Id, string Title, SpecificationKind Kind, int EndpointCount, DateTimeOffset UpdatedAt, DateTimeOffset? ProvisionedAt);
