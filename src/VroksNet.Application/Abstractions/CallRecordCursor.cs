namespace VroksNet.Application.Abstractions;

/// <summary>
/// Keyset position in the newest-first call history: a page continues with records strictly
/// older than (<see cref="Timestamp"/>, <see cref="Id"/>). The id breaks ties between records
/// logged within the same instant, so paging never skips or repeats one.
/// </summary>
public sealed record CallRecordCursor(DateTimeOffset Timestamp, Guid Id);
