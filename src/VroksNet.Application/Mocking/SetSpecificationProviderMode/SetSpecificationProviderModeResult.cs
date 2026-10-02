namespace VroksNet.Application.Mocking.SetSpecificationProviderMode;

/// <summary>The operations now served at their real path, and those that were skipped with why (overlaps). <see cref="Refusal"/> is set (UI-safe) when the change couldn't be saved as computed.</summary>
public sealed record SetSpecificationProviderModeResult(IReadOnlyList<string> Served, IReadOnlyList<SkippedOperation> Skipped, string? Refusal = null);
