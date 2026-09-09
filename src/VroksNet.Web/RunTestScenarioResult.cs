namespace VroksNet.Web;

public sealed record RunTestScenarioResult(bool Success, string Message, string? ResponseBody);
