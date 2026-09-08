using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace VroksNet.Infrastructure.Persistence;

/// <summary>The single writer that owns every database write, dequeued from <see cref="DbWriteQueue"/> one at a time.</summary>
public sealed class DbWriteBackgroundService(
    DbWriteQueue queue,
    IDbContextFactory<VroksNetDbContext> contextFactory,
    ILogger<DbWriteBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var context = await contextFactory.CreateDbContextAsync(stoppingToken);
                await job.Operation(context, stoppingToken);
                job.Completion.TrySetResult();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Serialized database write failed.");
                job.Completion.TrySetException(ex);
            }
        }
    }
}
