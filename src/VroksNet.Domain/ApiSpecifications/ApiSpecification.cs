namespace VroksNet.Domain.ApiSpecifications;

/// <summary>
/// A loaded OpenAPI, AsyncAPI or .proto specification. <see cref="Title"/> (the spec's own
/// <c>info.title</c>, or for .proto the <see cref="ProtoTitle"/>) is the identity used when a re-uploaded spec should replace this one,
/// rather than the file name — see docs/project-brief.md section 2 "Specification
/// versioning".
/// </summary>
public sealed class ApiSpecification
{
    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public SpecificationKind Kind { get; set; }

    /// <summary>The original uploaded YAML, kept verbatim for re-parsing/debugging; for .proto, the text of the file that declares the services (<see cref="ProtoPath"/>).</summary>
    public string RawContent { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// When provisioning (ADR 0001) last brought this specification in line with the manifest or the specs
    /// folder; null for one created in the UI or the API. A provisioned object is overwritten on
    /// every start — UI edits to it don't stick.
    /// </summary>
    public DateTimeOffset? ProvisionedAt { get; set; }

    /// <summary>
    /// AsyncAPI only: the distinct <c>servers.*.protocol</c> values the spec declares, lower-case, in
    /// order (<c>kafka</c>, <c>kafka-secure</c>) — what kind of connection its operations are meant
    /// for (<see cref="Connections.ServiceTypeTraits.TypesFor"/>). Empty for OpenAPI, for a spec
    /// without servers, and for one imported before this was recorded until it's imported again.
    /// </summary>
    public List<string> Protocols { get; set; } = [];

    /// <summary>
    /// .proto only (ADR 0004): the path of the file that declares the services, as it was imported
    /// (relative to the provisioning <c>specs</c> folder, or within an upload) — its imports resolve
    /// against that same root. Null for OpenAPI and AsyncAPI.
    /// </summary>
    public string? ProtoPath { get; set; }

    /// <summary>
    /// .proto only: the text of every file <see cref="ProtoPath"/> imports, directly or not, keyed by
    /// path — except the well-known types (<c>google/protobuf/*.proto</c>), which come with VroksNet.
    /// With <see cref="RawContent"/> it's everything needed to parse the spec again or export it.
    /// Empty for OpenAPI and AsyncAPI.
    /// </summary>
    public Dictionary<string, string> ProtoImports { get; set; } = [];

    /// <summary>
    /// .proto only: the parsed spec as a serialized <c>google.protobuf.FileDescriptorSet</c> — its
    /// file and everything it imports, the well-known types included, each file after its imports —
    /// so messages can be read and written without parsing the .proto text again. Null for OpenAPI
    /// and AsyncAPI.
    /// </summary>
    public byte[]? ProtoDescriptorSet { get; set; }

    public ICollection<MockEndpoints.MockEndpoint> Endpoints { get; set; } = new List<MockEndpoints.MockEndpoint>();

    /// <summary>
    /// Brings this (already stored) specification in line with a fresh import of the same title,
    /// as an idempotent update rather than a replacement:
    /// <list type="bullet">
    /// <item>an operation whose <see cref="MockEndpoints.MockEndpoint.OperationKey"/> is still in
    /// <paramref name="imported"/> is updated in place — its spec-derived content (example,
    /// status, schemas) is refreshed, while its id, <c>IsEnabled</c> and <c>ServeAtRealPath</c>
    /// are kept, so Test Scenarios, Publishers and call records that reference it keep working;</item>
    /// <item>a new operation is added; one no longer in the spec is removed.</item>
    /// </list>
    /// Operations sharing a key (possible in AsyncAPI: two "send" operations on one channel) are
    /// paired by their order of appearance (<see cref="MockEndpoints.MockEndpoint.Position"/>).
    /// A field is only written when its value differs, so importing the same file again changes
    /// nothing and reports <see cref="ReimportChanges.AnyChange"/> = false. A renamed operation
    /// can't be told apart from a removed one plus a new one.
    /// </summary>
    public ReimportChanges ApplyReimport(ApiSpecification imported)
    {
        var anyChange = false;
        if (Kind != imported.Kind)
        {
            Kind = imported.Kind;
            anyChange = true;
        }

        if (!string.Equals(RawContent, imported.RawContent, StringComparison.Ordinal))
        {
            RawContent = imported.RawContent;
            anyChange = true;
        }

        if (!string.Equals(ProtoPath, imported.ProtoPath, StringComparison.Ordinal))
        {
            ProtoPath = imported.ProtoPath;
            anyChange = true;
        }

        if (ProtoImports.Count != imported.ProtoImports.Count || ProtoImports.Except(imported.ProtoImports).Any())
        {
            ProtoImports = new Dictionary<string, string>(imported.ProtoImports);
            anyChange = true;
        }

        if (!(ProtoDescriptorSet ?? []).AsSpan().SequenceEqual(imported.ProtoDescriptorSet ?? []) || (ProtoDescriptorSet is null) != (imported.ProtoDescriptorSet is null))
        {
            ProtoDescriptorSet = imported.ProtoDescriptorSet;
            anyChange = true;
        }

        // Also fills it in for a spec imported before protocols were recorded, from the same file.
        if (!Protocols.SequenceEqual(imported.Protocols, StringComparer.Ordinal))
        {
            Protocols = [.. imported.Protocols];
            anyChange = true;
        }

        // Ordered by position so operations sharing a key pair up in the order they appear in the
        // spec — the order they're loaded in is arbitrary.
        var existingByKey = Endpoints
            .OrderBy(endpoint => endpoint.Position)
            .GroupBy(endpoint => endpoint.OperationKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => new Queue<MockEndpoints.MockEndpoint>(group), StringComparer.Ordinal);

        var kept = new HashSet<MockEndpoints.MockEndpoint>(ReferenceEqualityComparer.Instance);
        var added = new List<MockEndpoints.MockEndpoint>();
        foreach (var incoming in imported.Endpoints)
        {
            if (existingByKey.TryGetValue(incoming.OperationKey, out var candidates) && candidates.TryDequeue(out var existing))
            {
                anyChange |= existing.RefreshFrom(incoming);
                kept.Add(existing);
            }
            else
            {
                incoming.SpecificationId = Id;
                added.Add(incoming);
            }
        }

        var removed = Endpoints.Where(endpoint => !kept.Contains(endpoint)).ToList();
        foreach (var endpoint in removed)
        {
            Endpoints.Remove(endpoint);
        }

        foreach (var endpoint in added)
        {
            Endpoints.Add(endpoint);
        }

        if (added.Count > 0 || removed.Count > 0)
        {
            anyChange = true;
        }

        if (anyChange)
        {
            UpdatedAt = imported.UpdatedAt;
        }

        return new ReimportChanges(added, removed, anyChange);
    }
}
