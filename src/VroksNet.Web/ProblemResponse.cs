namespace VroksNet.Web;

/// <summary>The part of an RFC 7807 ProblemDetails body the UI shows.</summary>
public sealed record ProblemResponse(string? Title, string? Detail);
