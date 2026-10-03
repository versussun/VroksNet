namespace VroksNet.Application.Provisioning;

/// <summary>
/// The latest <see cref="ProvisioningReport"/>, shared by the provisioning run, the health check
/// and the system-info endpoint. A singleton; reports are immutable and swapped whole.
/// It starts as <see cref="ProvisioningStatus.Applying"/>, so the app isn't reported ready before
/// provisioning has had its turn.
/// </summary>
public sealed class ProvisioningState
{
    private ProvisioningReport _current = ProvisioningReport.NotStarted;

    public ProvisioningReport Current => Volatile.Read(ref _current);

    public void Set(ProvisioningReport report) => Volatile.Write(ref _current, report);
}
