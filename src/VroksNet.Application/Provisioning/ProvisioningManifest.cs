namespace VroksNet.Application.Provisioning;

/// <summary>
/// <c>vroksnet.yaml</c> (docs/schemas/provisioning-manifest.v1.schema.json), already validated
/// against its schema, with every connection's <c>valueFrom</c> resolved to its value.
/// </summary>
public sealed record ProvisioningManifest(
    IReadOnlyList<ManifestConnection> Connections,
    IReadOnlyList<ManifestSpecification> Specifications,
    IReadOnlyList<ManifestPublisher> Publishers,
    IReadOnlyList<ManifestTestScenario> TestScenarios)
{
    public static ProvisioningManifest Empty { get; } = new([], [], [], []);
}
