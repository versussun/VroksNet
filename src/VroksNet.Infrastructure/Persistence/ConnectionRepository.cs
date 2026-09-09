using Microsoft.EntityFrameworkCore;
using VroksNet.Application.Abstractions;
using VroksNet.Domain.Connections;

namespace VroksNet.Infrastructure.Persistence;

public sealed class ConnectionRepository(
    IDbContextFactory<VroksNetDbContext> contextFactory,
    IDbWriteQueue writeQueue) : IConnectionRepository
{
    public async Task<IReadOnlyList<Connection>> ListAsync(CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Connections
            .AsNoTracking()
            .OrderBy(connection => connection.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<Connection?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Connections
            .AsNoTracking()
            .FirstOrDefaultAsync(connection => connection.Id == id, cancellationToken);
    }

    public Task InsertAsync(Connection connection, CancellationToken cancellationToken)
    {
        return writeQueue.EnqueueAsync(async (context, ct) =>
        {
            context.Connections.Add(connection);
            await context.SaveChangesAsync(ct);
        }, cancellationToken);
    }

    public async Task<bool> UpdateAsync(Connection connection, CancellationToken cancellationToken)
    {
        var updated = false;

        await writeQueue.EnqueueAsync(async (context, ct) =>
        {
            var rows = await context.Connections
                .Where(c => c.Id == connection.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(c => c.Name, connection.Name)
                    .SetProperty(c => c.ServiceType, connection.ServiceType)
                    .SetProperty(c => c.Value, connection.Value)
                    .SetProperty(c => c.UpdatedAt, connection.UpdatedAt), ct);

            updated = rows > 0;
        }, cancellationToken);

        return updated;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var deleted = false;

        await writeQueue.EnqueueAsync(async (context, ct) =>
        {
            var rows = await context.Connections.Where(c => c.Id == id).ExecuteDeleteAsync(ct);
            deleted = rows > 0;
        }, cancellationToken);

        return deleted;
    }
}
