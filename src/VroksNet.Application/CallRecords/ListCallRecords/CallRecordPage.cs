namespace VroksNet.Application.CallRecords.ListCallRecords;

/// <summary><see cref="NextCursor"/> is null when there are no older records to load.</summary>
public sealed record CallRecordPage(IReadOnlyList<CallRecordSummary> Items, string? NextCursor);
