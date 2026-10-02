using Microsoft.EntityFrameworkCore;
using VroksNet.Application.Abstractions;
using VroksNet.Domain.ApiSpecifications;

namespace VroksNet.Infrastructure.Persistence;

public sealed class ApiSpecificationRepository(
    IDbContextFactory<VroksNetDbContext> contextFactory,
    IDbWriteQueue writeQueue) : IApiSpecificationRepository
{
    public async Task<IReadOnlyList<ApiSpecification>> ListAsync(CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.ApiSpecifications
            .Include(specification => specification.Endpoints)
            .AsNoTracking()
            .OrderBy(specification => specification.Title)
            .ToListAsync(cancellationToken);
    }

    public async Task<ApiSpecification?> FindByTitleAsync(string title, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.ApiSpecifications
            .Include(specification => specification.Endpoints)
            .AsNoTracking()
            .FirstOrDefaultAsync(specification => specification.Title == title, cancellationToken);
    }

    public async Task<ApiSpecification?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.ApiSpecifications
            .Include(specification => specification.Endpoints)
            .AsNoTracking()
            .FirstOrDefaultAsync(specification => specification.Id == id, cancellationToken);
    }

    public async Task<int> SetServeAtRealPathAsync(IReadOnlyCollection<Guid> mockEndpointIds, bool serveAtRealPath, CancellationToken cancellationToken)
    {
        if (mockEndpointIds.Count == 0)
        {
            return 0;
        }

        var updated = 0;
        await writeQueue.EnqueueAsync(async (context, ct) =>
        {
            updated = await context.MockEndpoints
                .Where(endpoint => mockEndpointIds.Contains(endpoint.Id))
                .ExecuteUpdateAsync(setters => setters.SetProperty(endpoint => endpoint.ServeAtRealPath, serveAtRealPath), ct);
        }, cancellationToken);

        return updated;
    }

    public async Task<int> SetEnabledAsync(IReadOnlyCollection<Guid> mockEndpointIds, bool enabled, CancellationToken cancellationToken)
    {
        if (mockEndpointIds.Count == 0)
        {
            return 0;
        }

        var updated = 0;
        await writeQueue.EnqueueAsync(async (context, ct) =>
        {
            updated = await context.MockEndpoints
                .Where(endpoint => mockEndpointIds.Contains(endpoint.Id))
                .ExecuteUpdateAsync(setters => setters.SetProperty(endpoint => endpoint.IsEnabled, enabled), ct);
        }, cancellationToken);

        return updated;
    }

    public Task UpsertAsync(ApiSpecification specification, CancellationToken cancellationToken)
    {
        return writeQueue.EnqueueAsync(async (context, ct) =>
        {
            // "Replace" per docs/project-brief.md section 2 means exactly that — no merge, no
            // version history. Deleting by title (cascades to its MockEndpoints) and inserting
            // fresh avoids reattaching a partially-detached entity graph.
            await context.ApiSpecifications
                .Where(s => s.Title == specification.Title)
                .ExecuteDeleteAsync(ct);

            context.ApiSpecifications.Add(specification);
            await context.SaveChangesAsync(ct);
        }, cancellationToken);
    }
}
