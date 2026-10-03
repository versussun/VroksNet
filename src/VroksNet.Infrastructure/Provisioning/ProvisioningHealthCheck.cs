using Microsoft.Extensions.Diagnostics.HealthChecks;
using VroksNet.Application.Provisioning;

namespace VroksNet.Infrastructure.Provisioning;

/// <summary>
/// Part of <c>/health</c> (readiness, docs/container-contract.md §6): Unhealthy until provisioning
/// has run, so Aspire's <c>WaitFor</c> and a Kubernetes readiness probe wait for the mocks to be
/// loaded. Failed with <c>Provisioning:FailOnError=false</c> is Degraded — the app keeps serving
/// what did apply, and /health still answers 200.
/// </summary>
public sealed class ProvisioningHealthCheck(ProvisioningState state) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var report = state.Current;
        return Task.FromResult(report.Status switch
        {
            ProvisioningStatus.NotConfigured => HealthCheckResult.Healthy("No provisioning directory."),
            ProvisioningStatus.Applied => HealthCheckResult.Healthy("Provisioning applied."),
            ProvisioningStatus.Failed => HealthCheckResult.Degraded($"Provisioning failed: {report.Errors.Count} error(s)."),
            _ => HealthCheckResult.Unhealthy("Provisioning hasn't finished yet.")
        });
    }
}
