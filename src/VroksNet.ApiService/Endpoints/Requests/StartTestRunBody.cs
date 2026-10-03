namespace VroksNet.ApiService.Endpoints.Requests;

/// <summary>
/// Optional body of <c>POST /api/test-scenarios/{id}/runs</c>: <see cref="RunAt"/> (a time with an
/// offset) or <see cref="DelaySeconds"/> makes it a delayed run. No body runs it now.
/// </summary>
public sealed record StartTestRunBody(DateTimeOffset? RunAt, int? DelaySeconds);
