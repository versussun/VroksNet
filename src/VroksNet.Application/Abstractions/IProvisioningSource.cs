using VroksNet.Application.Provisioning;

namespace VroksNet.Application.Abstractions;

/// <summary>Reads the provisioning directory (ADR 0001, docs/container-contract.md §4).</summary>
public interface IProvisioningSource
{
    /// <summary>Null when provisioning isn't configured — the directory doesn't exist.</summary>
    Task<ProvisioningInput?> LoadAsync(CancellationToken cancellationToken);
}
