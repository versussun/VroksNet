namespace VroksNet.Application.Provisioning.ExportProvisioning;

/// <summary>
/// What an export holds, in manifest order. <see cref="Notes"/> are what the reader should know —
/// objects left out because something they refer to is gone, settings the manifest can't express.
/// </summary>
public sealed record ProvisioningExport(
    string AppVersion,
    DateTimeOffset ExportedAt,
    IReadOnlyList<ExportedSpecification> Specifications,
    IReadOnlyList<ExportedConnection> Connections,
    IReadOnlyList<ManifestSpecification> SpecificationSettings,
    IReadOnlyList<ManifestPublisher> Publishers,
    IReadOnlyList<ManifestTestScenario> TestScenarios,
    IReadOnlyList<ManifestTestSuite> TestSuites,
    IReadOnlyList<string> Notes);

/// <summary>A spec as it was imported; <see cref="FileName"/> is unique within the export.</summary>
public sealed record ExportedSpecification(string FileName, string Content);

/// <summary>Exactly one of <see cref="Value"/> and <see cref="ValueFrom"/> is set.</summary>
public sealed record ExportedConnection(string Name, Domain.Connections.ConnectionServiceType Type, string? Value, string? ValueFrom);
