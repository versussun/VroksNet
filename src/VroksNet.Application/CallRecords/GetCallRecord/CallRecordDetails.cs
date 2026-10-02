namespace VroksNet.Application.CallRecords.GetCallRecord;

/// <summary>The bodies stored for one call, each capped at <see cref="CallRecordSnapshot.MaxLength"/> characters when it was logged.</summary>
public sealed record CallRecordDetails(Guid Id, string? RequestSnapshot, string? ResponseSnapshot);
