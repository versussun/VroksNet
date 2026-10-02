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

    public async Task<Guid> UpsertAsync(ApiSpecification imported, CancellationToken cancellationToken)
    {
        var storedId = imported.Id;
        await writeQueue.EnqueueAsync(async (context, ct) =>
        {
            // Loaded and changed in this one context — never attach the detached `imported` graph
            // onto it, which is what used to throw DbUpdateConcurrencyException.
            var existing = await context.ApiSpecifications
                .Include(specification => specification.Endpoints)
                .FirstOrDefaultAsync(specification => specification.Title == imported.Title, ct);

            if (existing is null)
            {
                context.ApiSpecifications.Add(imported);
                await context.SaveChangesAsync(ct);
                return;
            }

            storedId = existing.Id;
            var changes = existing.ApplyReimport(imported);
            if (!changes.AnyChange)
            {
                return;
            }

            // State the inserts and deletes explicitly instead of leaving them to DetectChanges:
            // a new endpoint already has its (client-generated) id, which EF would otherwise take
            // for an existing row.
            context.MockEndpoints.AddRange(changes.Added);
            context.MockEndpoints.RemoveRange(changes.Removed);
            await context.SaveChangesAsync(ct);
        }, cancellationToken);

        return storedId;
    }
}
