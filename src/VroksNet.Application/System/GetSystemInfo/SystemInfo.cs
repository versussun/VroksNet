using VroksNet.Application.Provisioning;

namespace VroksNet.Application.System.GetSystemInfo;

/// <summary>
/// What <c>GET /api/system/info</c> returns (docs/container-contract.md §6): the app's version,
/// the container-contract version it implements, and provisioning's latest report.
/// </summary>
public sealed record SystemInfo(string Version, int ContractVersion, ProvisioningReport Provisioning);
