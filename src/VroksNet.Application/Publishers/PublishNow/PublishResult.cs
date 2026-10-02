using VroksNet.Application.Abstractions;

namespace VroksNet.Application.Publishers.PublishNow;

/// <summary>
/// <see cref="Success"/> means the message went out. <see cref="Message"/> is UI-safe.
/// <see cref="Payload"/> is what was published, placeholders filled in. <see cref="ContractValidation"/>
/// checks it against the operation's payload schema — null when there's none (or nothing was sent);
/// a mismatch is reported but doesn't fail the publish, since it's the spec's own example (or the
/// user's override) that's off.
/// </summary>
public sealed record PublishResult(bool Success, string Message, string? Payload, SchemaValidationResult? ContractValidation);
