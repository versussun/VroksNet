namespace VroksNet.Application.Mocking.InvokeMockEndpoint;

/// <summary>
/// The outcome of matching a request against the imported specs' enabled endpoints.
/// <see cref="ExampleJson"/> is the spec's own static example — no placeholder substitution yet
/// (see docs/project-brief.md section 2 "Динамика ответов"); null means the operation matched but
/// the spec had no example for it. <see cref="ResponseBody"/> is what the mock actually answers
/// with: the example, a "{}" stand-in when there's none, or a not-found message when nothing matched.
/// </summary>
public sealed record MockInvocationResult(bool Matched, string? OperationKey, string? ExampleJson, string ResponseBody);
