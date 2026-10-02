using Microsoft.EntityFrameworkCore;
using VroksNet.Application.Abstractions;
using VroksNet.Domain.TestScenarios;

namespace VroksNet.Infrastructure.Persistence;

public sealed class TestScenarioRepository(
    IDbContextFactory<VroksNetDbContext> contextFactory,
    IDbWriteQueue writeQueue) : ITestScenarioRepository
{
    public async Task<IReadOnlyList<TestScenario>> ListAsync(CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.TestScenarios
            .AsNoTracking()
            .OrderBy(scenario => scenario.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<TestScenario?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.TestScenarios
            .AsNoTracking()
            .FirstOrDefaultAsync(scenario => scenario.Id == id, cancellationToken);
    }

    public Task InsertAsync(TestScenario scenario, CancellationToken cancellationToken)
    {
        return writeQueue.EnqueueAsync(async (context, ct) =>
        {
            context.TestScenarios.Add(scenario);
            await context.SaveChangesAsync(ct);
        }, cancellationToken);
    }

    public async Task<bool> UpdateAsync(TestScenario scenario, CancellationToken cancellationToken)
    {
        var updated = false;

        await writeQueue.EnqueueAsync(async (context, ct) =>
        {
            var rows = await context.TestScenarios
                .Where(s => s.Id == scenario.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(s => s.Name, scenario.Name)
                    .SetProperty(s => s.SpecificationId, scenario.SpecificationId)
                    .SetProperty(s => s.MockEndpointId, scenario.MockEndpointId)
                    .SetProperty(s => s.ConnectionId, scenario.ConnectionId)
                    .SetProperty(s => s.PayloadOverride, scenario.PayloadOverride)
                    .SetProperty(s => s.Kind, scenario.Kind)
                    .SetProperty(s => s.ListenTimeoutSeconds, scenario.ListenTimeoutSeconds)
                    .SetProperty(s => s.Exchange, scenario.Exchange)
                    .SetProperty(s => s.UpdatedAt, scenario.UpdatedAt), ct);

            updated = rows > 0;
        }, cancellationToken);

        return updated;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var deleted = false;

        await writeQueue.EnqueueAsync(async (context, ct) =>
        {
            var rows = await context.TestScenarios.Where(s => s.Id == id).ExecuteDeleteAsync(ct);
            deleted = rows > 0;
        }, cancellationToken);

        return deleted;
    }

    public Task RecordRunAsync(Guid id, DateTimeOffset ranAt, bool success, string message, CancellationToken cancellationToken)
    {
        return writeQueue.EnqueueAsync(async (context, ct) =>
        {
            await context.TestScenarios
                .Where(s => s.Id == id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(s => s.LastRunAt, ranAt)
                    .SetProperty(s => s.LastRunSuccess, success)
                    .SetProperty(s => s.LastRunMessage, message), ct);
        }, cancellationToken);
    }
}
