using VroksNet.Domain.CallRecords;

namespace VroksNet.Application.Abstractions;

public interface ICallRecordRepository
{
    Task InsertAsync(CallRecord record, CancellationToken cancellationToken);

    /// <summary>
    /// Up to <paramref name="limit"/> records matching <paramref name="filter"/>, newest first
    /// (by <see cref="CallRecord.Timestamp"/>, then <see cref="CallRecord.Id"/>), starting strictly
    /// after <paramref name="after"/> when given.
    /// </summary>
    Task<IReadOnlyList<CallRecord>> ListAsync(CallRecordFilter filter, CallRecordCursor? after, int limit, CancellationToken cancellationToken);

    Task<CallRecord?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <returns>How many records were deleted.</returns>
    Task<int> DeleteAllAsync(CancellationToken cancellationToken);
}
