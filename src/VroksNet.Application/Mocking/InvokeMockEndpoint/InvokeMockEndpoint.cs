using Mediator;

namespace VroksNet.Application.Mocking.InvokeMockEndpoint;

/// <summary>
/// A request to the mocked API surface itself (e.g. "GET" + "/pets/1") — as opposed to the admin
/// CRUD requests under Specifications. <see cref="ProviderMode"/> is true for a request that came in
/// on the provider port at its real path, which only operations with
/// <see cref="Domain.MockEndpoints.MockEndpoint.ServeAtRealPath"/> answer; false for the "/mock"
/// prefix, which every enabled operation answers. <see cref="QueryString"/> (e.g. "?x=1", or empty)
/// and <see cref="Body"/> don't affect matching; the body is validated against the operation's
/// request schema, logged to the call history and read by <c>{{request.body.*}}</c> placeholders, so
/// the caller only needs to pass the first <see cref="CallRecords.CallRecordSnapshot.MaxLength"/> + 1
/// characters of a large body (enough to know it was cut). <see cref="ContentType"/> decides whether
/// the body is validated at all — the stored request schema is the spec's application/json one.
/// <see cref="QueryParameters"/> and <see cref="Headers"/> (repeated values comma-joined) only feed
/// the response's <c>{{request.query.*}}</c>/<c>{{request.header.*}}</c> placeholders. A HEAD
/// request matches the GET operation for the same path.
/// </summary>
public sealed record InvokeMockEndpoint(
    string Method,
    string Path,
    string QueryString = "",
    string? Body = null,
    bool ProviderMode = false,
    string? ContentType = null,
    IReadOnlyDictionary<string, string>? QueryParameters = null,
    IReadOnlyDictionary<string, string>? Headers = null) : IRequest<MockInvocationResult>;
