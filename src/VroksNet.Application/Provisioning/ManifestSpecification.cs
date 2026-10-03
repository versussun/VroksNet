namespace VroksNet.Application.Provisioning;

/// <summary>Settings for an imported spec, found by its <c>info.title</c>. Operations not listed in <see cref="DisabledOperations"/> are enabled.</summary>
public sealed record ManifestSpecification(string Title, bool ProviderMode, IReadOnlyList<string> DisabledOperations);
