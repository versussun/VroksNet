namespace VroksNet.Application.CallRecords;

/// <summary>
/// Caps the request/response bodies stored in the call history, so one large payload can't bloat
/// the database or every history page. Anything longer keeps its first <see cref="MaxLength"/>
/// characters plus a marker saying it was cut.
/// </summary>
public static class CallRecordSnapshot
{
    /// <summary>64K characters — plenty for any JSON a person would read in the history.</summary>
    public const int MaxLength = 64 * 1024;

    public const string TruncatedMarker = "\n…(truncated)";

    public static string? Truncate(string? body)
        => body is null || body.Length <= MaxLength ? body : string.Concat(body.AsSpan(0, MaxLength), TruncatedMarker);
}
