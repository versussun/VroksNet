namespace VroksNet.Application.Provisioning;

/// <summary>Provisioning's state at a point in time — what the health check and <c>GET /api/system/info</c> report.</summary>
public sealed record ProvisioningReport(
    ProvisioningStatus Status,
    string? Source,
    DateTimeOffset? AppliedAt,
    ProvisioningCounts Counts,
    IReadOnlyList<ProvisioningError> Errors)
{
    public static ProvisioningReport NotStarted { get; } = new(ProvisioningStatus.Applying, null, null, ProvisioningCounts.None, []);
}
