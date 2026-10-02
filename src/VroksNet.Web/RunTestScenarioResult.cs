namespace VroksNet.Web;

/// <summary><see cref="ContractValidation"/> is null when the run's response wasn't checked against the spec (a broker publish, no response, or nothing declared to check against).</summary>
public sealed record RunTestScenarioResult(
    bool Success,
    string Message,
    string? ResponseBody,
    int? StatusCode = null,
    ContractValidationResult? ContractValidation = null);
