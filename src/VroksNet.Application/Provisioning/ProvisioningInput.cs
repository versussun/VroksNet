namespace VroksNet.Application.Provisioning;

/// <summary>
/// What the provisioning directory holds. <see cref="Errors"/> are problems found while reading it
/// (an unreadable file, a spec of unknown kind, a manifest that fails its schema, an unset
/// <c>valueFrom</c>); anything that read fine is still listed.
/// </summary>
public sealed record ProvisioningInput(
    string Source,
    IReadOnlyList<ProvisioningSpecFile> Specifications,
    ProvisioningManifest Manifest,
    IReadOnlyList<ProvisioningError> Errors);
