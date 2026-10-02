using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using VroksNet.Infrastructure.Persistence;

namespace VroksNet.UnitTests.Persistence;

/// <summary>The database part of <c>/health</c>, against a real temp-file SQLite database and a missing one.</summary>
public sealed class DatabaseHealthCheckTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"vroksnet-health-test-{Guid.NewGuid():N}.db");

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync()
    {
        // Microsoft.Data.Sqlite pools the native connection, so it can still hold the file open.
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task CheckHealth_DatabaseExists_ReturnsHealthy()
    {
        // As startup's migrations leave it: CanConnect on SQLite checks the file exists, it doesn't create it.
        await using (var context = new VroksNetDbContext(OptionsFor($"Data Source={_dbPath}")))
        {
            await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        }

        var result = await CheckAsync($"Data Source={_dbPath}");

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task CheckHealth_DatabaseMissing_ReturnsRegisteredFailureStatus()
    {
        var unreachablePath = Path.Combine(Path.GetTempPath(), $"vroksnet-missing-{Guid.NewGuid():N}", "vroksnet.db");

        var result = await CheckAsync($"Data Source={unreachablePath}");

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("Can't connect to the database.", result.Description);
    }

    private static async Task<HealthCheckResult> CheckAsync(string connectionString)
    {
        var healthCheck = new DatabaseHealthCheck(new PooledDbContextFactory<VroksNetDbContext>(OptionsFor(connectionString)));
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("database", healthCheck, HealthStatus.Unhealthy, tags: null)
        };

        return await healthCheck.CheckHealthAsync(context, TestContext.Current.CancellationToken);
    }

    private static DbContextOptions<VroksNetDbContext> OptionsFor(string connectionString)
        => new DbContextOptionsBuilder<VroksNetDbContext>().UseSqlite(connectionString).Options;
}
