using Microsoft.EntityFrameworkCore;
using VroksNet.Application.Abstractions;
using VroksNet.Domain.CallRecords;

namespace VroksNet.Infrastructure.Persistence;

public sealed class CallRecordRepository(
    IDbContextFactory<VroksNetDbContext> contextFactory,
    IDbWriteQueue writeQueue) : ICallRecordRepository
{
    public Task InsertAsync(CallRecord record, CancellationToken cancellationToken)
    {
        return writeQueue.EnqueueAsync(async (context, ct) =>
        {
            context.CallRecords.Add(record);
            await context.SaveChangesAsync(ct);
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<CallRecord>> ListAsync(CallRecordFilter filter, CallRecordCursor? after, int limit, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.CallRecords.AsNoTracking();

        if (filter.SpecificationId is { } specificationId)
        {
            query = query.Where(record => record.SpecificationId == specificationId);
        }

        if (filter.MockEndpointId is { } mockEndpointId)
        {
            query = query.Where(record => record.MockEndpointId == mockEndpointId);
        }

        if (filter.PublisherId is { } publisherId)
        {
            query = query.Where(record => record.PublisherId == publisherId);
        }

        if (filter.TestScenarioId is { } testScenarioId)
        {
            query = query.Where(record => record.TestScenarioId == testScenarioId);
        }

        if (filter.Direction is { } direction)
        {
            query = query.Where(record => record.Direction == direction);
        }

        if (filter.ContractValid is { } contractValid)
        {
            query = query.Where(record => record.ContractValid == contractValid);
        }

        if (after is not null)
        {
            query = query.Where(record => record.Timestamp < after.Timestamp
                || (record.Timestamp == after.Timestamp && record.Id.CompareTo(after.Id) < 0));
        }

        return await query
            .OrderByDescending(record => record.Timestamp)
            .ThenByDescending(record => record.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<CallRecord?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.CallRecords.AsNoTracking().FirstOrDefaultAsync(record => record.Id == id, cancellationToken);
    }

    public async Task<int> DeleteAllAsync(CancellationToken cancellationToken)
    {
        var deleted = 0;

        await writeQueue.EnqueueAsync(async (context, ct) =>
        {
            deleted = await context.CallRecords.ExecuteDeleteAsync(ct);
        }, cancellationToken);

        return deleted;
    }
}
