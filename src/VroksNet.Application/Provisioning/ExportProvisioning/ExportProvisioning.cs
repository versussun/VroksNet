using Mediator;

namespace VroksNet.Application.Provisioning.ExportProvisioning;

/// <summary>
/// The current configuration as a provisioning directory (ADR 0001, "Export the current
/// configuration"), zipped: configure in the UI, export, commit, provision from it. Connection
/// values are left out — each becomes a <c>valueFrom</c> — unless <see cref="InlineConnectionValues"/>.
/// </summary>
public sealed record ExportProvisioning(bool InlineConnectionValues = false) : IRequest<byte[]>;
