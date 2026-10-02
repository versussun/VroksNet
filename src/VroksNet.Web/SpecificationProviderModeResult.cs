namespace VroksNet.Web;

/// <summary>Operations now served at their real path, and those skipped with why (overlaps).</summary>
public sealed record SpecificationProviderModeResult(string[] Served, SkippedOperation[] Skipped, string? Refusal = null);
