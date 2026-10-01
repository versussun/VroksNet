namespace VroksNet.Web;

/// <summary>Whether a run's response matched the spec; <see cref="Errors"/> lists the violations (empty when valid).</summary>
public sealed record ContractValidationResult(bool IsValid, string[] Errors);
