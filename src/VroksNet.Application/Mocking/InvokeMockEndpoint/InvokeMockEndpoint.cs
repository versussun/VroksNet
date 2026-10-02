using Mediator;

namespace VroksNet.Application.Mocking.InvokeMockEndpoint;

/// <summary>
/// A request to the mocked API surface itself (e.g. "GET" + "/pets/1") — as opposed to the admin
/// CRUD requests under Specifications. <see cref="QueryString"/> (e.g. "?x=1", or empty) and
/// <see cref="Body"/> don't affect matching yet; they're only logged to the call history, so the
/// caller only needs to pass the first <see cref="CallRecords.CallRecordSnapshot.MaxLength"/> + 1
/// characters of a large body (enough for the history to know it was cut).
/// </summary>
public sealed record InvokeMockEndpoint(string Method, string Path, string QueryString = "", string? Body = null) : IRequest<MockInvocationResult>;
