using VroksNet.Application.Abstractions;

namespace VroksNet.Application.TestScenarios.RunTestScenario;

/// <summary>
/// <see cref="Success"/> means the whole run passed: the send went through <em>and</em>, when it
/// was checked, the response matched the spec. <see cref="ContractValidation"/> says which of the
/// two failed — it's null when nothing was validated (a broker publish, a send that never got a
/// response, or an operation with no declared responses to check against).
/// </summary>
public sealed record RunTestScenarioResult(
    bool Success,
    string Message,
    string? ResponseBody,
    int? StatusCode = null,
    SchemaValidationResult? ContractValidation = null);
