using VroksNet.Domain.CallRecords;

namespace VroksNet.Application.Abstractions;

/// <summary>Write-only for now — nothing reads these back yet (see docs/project-brief.md Phase 04 "история и логи").</summary>
public interface ICallRecordRepository
{
    Task InsertAsync(CallRecord record, CancellationToken cancellationToken);
}
