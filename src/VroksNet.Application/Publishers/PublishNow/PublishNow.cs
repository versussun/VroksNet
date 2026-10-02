using Mediator;

namespace VroksNet.Application.Publishers.PublishNow;

/// <summary>
/// Publishes one message for the publisher — on demand (whether or not it's enabled), and also what
/// the background worker sends for each due publisher. Result is null if no publisher with that id exists.
/// </summary>
public sealed record PublishNow(Guid Id) : IRequest<PublishResult?>;
