using VroksNet.Application.Provisioning.ExportProvisioning;

namespace VroksNet.Application.Abstractions;

/// <summary>Writes a <see cref="ProvisioningExport"/> as a provisioning directory in a zip: <c>specs/…</c> and <c>vroksnet.yaml</c>.</summary>
public interface IProvisioningPackageWriter
{
    byte[] Write(ProvisioningExport export);
}
