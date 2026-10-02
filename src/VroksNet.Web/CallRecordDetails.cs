namespace VroksNet.Web;

/// <summary>The bodies stored for one call (each capped server-side, with a "…(truncated)" marker when cut).</summary>
public sealed record CallRecordDetails(Guid Id, string? RequestSnapshot, string? ResponseSnapshot);
