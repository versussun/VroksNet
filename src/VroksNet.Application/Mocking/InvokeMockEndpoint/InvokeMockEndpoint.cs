using Mediator;

namespace VroksNet.Application.Mocking.InvokeMockEndpoint;

/// <summary>A request to the mocked API surface itself (e.g. "GET" + "/pets/1") — as opposed to the admin CRUD requests under Specifications.</summary>
public sealed record InvokeMockEndpoint(string Method, string Path) : IRequest<MockInvocationResult>;
