using VroksNet.Application.Abstractions;
using VroksNet.Domain.CallRecords;

namespace VroksNet.Infrastructure.Persistence;

public sealed class CallRecordRepository(IDbWriteQueue writeQueue) : ICallRecordRepository
{
    public Task InsertAsync(CallRecord record, CancellationToken cancellationToken)
    {
        return writeQueue.EnqueueAsync(async (context, ct) =>
        {
            context.CallRecords.Add(record);
            await context.SaveChangesAsync(ct);
        }, cancellationToken);
    }
}
