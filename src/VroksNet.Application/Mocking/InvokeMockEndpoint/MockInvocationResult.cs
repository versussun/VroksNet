namespace VroksNet.Application.Mocking.InvokeMockEndpoint;

/// <summary>
/// The outcome of matching a request against the imported specs' enabled endpoints.
/// <see cref="ExampleJson"/> is the operation's example (the spec's own, or one built from its
/// schema at import), before placeholder substitution; null means the operation matched but has none. <see cref="ResponseBody"/> is what the
/// mock actually answers with: the example with its placeholders filled in, a "{}" stand-in when
/// there's none, empty for a status that carries no body (204, 304), or a not-found message when
/// nothing matched. <see cref="StatusCode"/> is the status to answer with — the spec's (see
/// <see cref="Domain.MockEndpoints.MockEndpoint.ExampleStatusCode"/>), or 404 when nothing matched.
/// </summary>
public sealed record MockInvocationResult(bool Matched, string? OperationKey, string? ExampleJson, string ResponseBody, int StatusCode);
