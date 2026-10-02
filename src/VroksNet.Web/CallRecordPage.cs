namespace VroksNet.Web;

/// <summary><see cref="NextCursor"/> is null when there are no older records to load.</summary>
public sealed record CallRecordPage(CallRecordSummary[] Items, string? NextCursor);
