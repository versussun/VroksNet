using Microsoft.EntityFrameworkCore;
using VroksNet.Application.Abstractions;
using VroksNet.Domain.Publishers;

namespace VroksNet.Infrastructure.Persistence;

public sealed class PublisherRepository(
    IDbContextFactory<VroksNetDbContext> contextFactory,
    IDbWriteQueue writeQueue) : IPublisherRepository
{
    public async Task<IReadOnlyList<Publisher>> ListAsync(CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Publishers
            .AsNoTracking()
            .OrderBy(publisher => publisher.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<Publisher?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Publishers
            .AsNoTracking()
            .FirstOrDefaultAsync(publisher => publisher.Id == id, cancellationToken);
    }

    public Task InsertAsync(Publisher publisher, CancellationToken cancellationToken)
    {
        return writeQueue.EnqueueAsync(async (context, ct) =>
        {
            context.Publishers.Add(publisher);
            await context.SaveChangesAsync(ct);
        }, cancellationToken);
    }

    public async Task<bool> UpdateAsync(Publisher publisher, CancellationToken cancellationToken)
    {
        var updated = false;

        await writeQueue.EnqueueAsync(async (context, ct) =>
        {
            var rows = await context.Publishers
                .Where(p => p.Id == publisher.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(p => p.Name, publisher.Name)
                    .SetProperty(p => p.SpecificationId, publisher.SpecificationId)
                    .SetProperty(p => p.MockEndpointId, publisher.MockEndpointId)
                    .SetProperty(p => p.ConnectionId, publisher.ConnectionId)
                    .SetProperty(p => p.PayloadOverride, publisher.PayloadOverride)
                    .SetProperty(p => p.Exchange, publisher.Exchange)
                    .SetProperty(p => p.IntervalSeconds, publisher.IntervalSeconds)
                    .SetProperty(p => p.UpdatedAt, publisher.UpdatedAt), ct);

            updated = rows > 0;
        }, cancellationToken);

        return updated;
    }

    public async Task<bool> SetEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken)
    {
        var updated = false;

        await writeQueue.EnqueueAsync(async (context, ct) =>
        {
            var rows = await context.Publishers
                .Where(p => p.Id == id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.IsEnabled, enabled), ct);
            updated = rows > 0;
        }, cancellationToken);

        return updated;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var deleted = false;

        await writeQueue.EnqueueAsync(async (context, ct) =>
        {
            var rows = await context.Publishers.Where(p => p.Id == id).ExecuteDeleteAsync(ct);
            deleted = rows > 0;
        }, cancellationToken);

        return deleted;
    }

    public Task RecordPublishAsync(Guid id, DateTimeOffset publishedAt, bool success, string message, CancellationToken cancellationToken)
    {
        return writeQueue.EnqueueAsync(async (context, ct) =>
        {
            await context.Publishers
                .Where(p => p.Id == id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(p => p.LastPublishedAt, publishedAt)
                    .SetProperty(p => p.LastPublishSuccess, success)
                    .SetProperty(p => p.LastPublishMessage, message), ct);
        }, cancellationToken);
    }
}
