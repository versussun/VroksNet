using Mediator;

namespace VroksNet.Application.Provisioning.ApplyProvisioning;

/// <summary>Applies the provisioning directory once (ADR 0001) — sent at startup. The report is also stored in <see cref="ProvisioningState"/>.</summary>
public sealed record ApplyProvisioning : IRequest<ProvisioningReport>;
