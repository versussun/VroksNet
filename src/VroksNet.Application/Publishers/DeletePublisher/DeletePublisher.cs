using Mediator;

namespace VroksNet.Application.Publishers.DeletePublisher;

/// <summary>Result is false if no publisher with that id exists. Its call-history records stay.</summary>
public sealed record DeletePublisher(Guid Id) : IRequest<bool>;
