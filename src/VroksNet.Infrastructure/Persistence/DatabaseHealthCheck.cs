using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace VroksNet.Infrastructure.Persistence;

/// <summary>
/// Readiness of the SQLite database behind <c>/health</c>: a short-lived context from the factory
/// (like every other read) must reach the database. On SQLite, <c>CanConnectAsync</c> checks that
/// the database exists rather than creating it — startup's migrations already have, so a missing
/// or unreadable file reads as unhealthy instead of being silently recreated empty. No exception
/// detail goes into the result: <c>/health</c> is unauthenticated.
/// </summary>
public sealed class DatabaseHealthCheck(IDbContextFactory<VroksNetDbContext> contextFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await dbContext.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : new HealthCheckResult(context.Registration.FailureStatus, "Can't connect to the database.");
    }
}
