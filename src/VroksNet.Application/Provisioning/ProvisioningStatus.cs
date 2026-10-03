namespace VroksNet.Application.Provisioning;

/// <summary>docs/container-contract.md §6: where provisioning is.</summary>
public enum ProvisioningStatus
{
    /// <summary>No provisioning directory — nothing to do.</summary>
    NotConfigured,
    Applying,
    Applied,

    /// <summary>Some of it couldn't be applied; see the errors.</summary>
    Failed
}
