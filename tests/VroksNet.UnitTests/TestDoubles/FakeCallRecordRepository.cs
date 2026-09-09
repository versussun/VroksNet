using VroksNet.Application.Abstractions;
using VroksNet.Domain.CallRecords;

namespace VroksNet.UnitTests.TestDoubles;

internal sealed class FakeCallRecordRepository : ICallRecordRepository
{
    public List<CallRecord> Inserted { get; } = [];

    public Task InsertAsync(CallRecord record, CancellationToken cancellationToken)
    {
        Inserted.Add(record);
        return Task.CompletedTask;
    }
}
