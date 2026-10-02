using VroksNet.Application.Abstractions;
using VroksNet.Domain.CallRecords;

namespace VroksNet.UnitTests.TestDoubles;

internal sealed class FakeCallRecordRepository : ICallRecordRepository
{
    public List<CallRecord> Inserted { get; } = [];

    public Task InsertAsync(CallRecord record, CancellationToken cancellationToken)
    {
        if (InsertFailure is not null)
        {
            return Task.FromException(InsertFailure);
        }

        Inserted.Add(record);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CallRecord>> ListAsync(CallRecordFilter filter, CallRecordCursor? after, int limit, CancellationToken cancellationToken)
    {
        IReadOnlyList<CallRecord> page = Inserted
            .Where(r => filter.SpecificationId is null || r.SpecificationId == filter.SpecificationId)
            .Where(r => filter.MockEndpointId is null || r.MockEndpointId == filter.MockEndpointId)
            .Where(r => filter.TestScenarioId is null || r.TestScenarioId == filter.TestScenarioId)
            .Where(r => filter.Direction is null || r.Direction == filter.Direction)
            .Where(r => filter.ContractValid is null || r.ContractValid == filter.ContractValid)
            .Where(r => after is null || r.Timestamp < after.Timestamp || (r.Timestamp == after.Timestamp && r.Id.CompareTo(after.Id) < 0))
            .OrderByDescending(r => r.Timestamp)
            .ThenByDescending(r => r.Id)
            .Take(limit)
            .ToList();
        return Task.FromResult(page);
    }

    /// <summary>When set, <see cref="InsertAsync"/> throws it — to test callers that must survive a failed history write.</summary>
    public Exception? InsertFailure { get; set; }

    public Task<CallRecord?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
        => Task.FromResult(Inserted.FirstOrDefault(r => r.Id == id));

    public Task<int> DeleteAllAsync(CancellationToken cancellationToken)
    {
        var count = Inserted.Count;
        Inserted.Clear();
        return Task.FromResult(count);
    }
}
