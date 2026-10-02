namespace VroksNet.Domain.ApiSpecifications;

/// <summary>
/// A loaded OpenAPI or AsyncAPI specification. <see cref="Title"/> (the spec's own
/// <c>info.title</c>) is the identity used when a re-uploaded spec should replace this one,
/// rather than the file name — see docs/project-brief.md section 2 "Specification
/// versioning".
/// </summary>
public sealed class ApiSpecification
{
    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public SpecificationKind Kind { get; set; }

    /// <summary>The original uploaded YAML, kept verbatim for re-parsing/debugging.</summary>
    public string RawContent { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<MockEndpoints.MockEndpoint> Endpoints { get; set; } = new List<MockEndpoints.MockEndpoint>();
}
