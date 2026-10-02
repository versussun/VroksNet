namespace VroksNet.Web;

/// <summary>A "publish now" outcome: <see cref="Payload"/> is what went out, placeholders filled in; <see cref="ContractValidation"/> is null when there was no payload schema to check it against.</summary>
public sealed record PublishResult(bool Success, string Message, string? Payload, ContractValidationResult? ContractValidation);
