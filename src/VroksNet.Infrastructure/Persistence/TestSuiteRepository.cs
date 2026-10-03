using Microsoft.EntityFrameworkCore;
using VroksNet.Application.Abstractions;
using VroksNet.Domain.TestSuites;

namespace VroksNet.Infrastructure.Persistence;

public sealed class TestSuiteRepository(
    IDbContextFactory<VroksNetDbContext> contextFactory,
    IDbWriteQueue writeQueue) : ITestSuiteRepository
{
    public async Task<IReadOnlyList<TestSuite>> ListAsync(CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.TestSuites.AsNoTracking().OrderBy(suite => suite.Name).ToListAsync(cancellationToken);
    }

    public async Task<TestSuite?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.TestSuites.AsNoTracking().FirstOrDefaultAsync(suite => suite.Id == id, cancellationToken);
    }

    public async Task<TestSuite?> FindByNameAsync(string name, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.TestSuites.AsNoTracking().FirstOrDefaultAsync(suite => suite.Name == name, cancellationToken);
    }

    public Task InsertAsync(TestSuite suite, CancellationToken cancellationToken)
    {
        return writeQueue.EnqueueAsync(async (context, ct) =>
        {
            context.TestSuites.Add(suite);
            await context.SaveChangesAsync(ct);
        }, cancellationToken);
    }

    public async Task<bool> UpdateAsync(TestSuite suite, CancellationToken cancellationToken)
    {
        var updated = false;
        await writeQueue.EnqueueAsync(async (context, ct) =>
        {
            // Loaded and saved rather than ExecuteUpdate: the scenario list is a converted column.
            var stored = await context.TestSuites.FirstOrDefaultAsync(s => s.Id == suite.Id, ct);
            if (stored is null)
            {
                return;
            }

            stored.Name = suite.Name;
            stored.TestScenarioIds = [.. suite.TestScenarioIds];
            stored.RunOnStartup = suite.RunOnStartup;
            stored.UpdatedAt = suite.UpdatedAt;
            await context.SaveChangesAsync(ct);
            updated = true;
        }, cancellationToken);

        return updated;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var deleted = false;
        await writeQueue.EnqueueAsync(async (context, ct) =>
        {
            deleted = await context.TestSuites.Where(suite => suite.Id == id).ExecuteDeleteAsync(ct) > 0;
        }, cancellationToken);

        return deleted;
    }
}
