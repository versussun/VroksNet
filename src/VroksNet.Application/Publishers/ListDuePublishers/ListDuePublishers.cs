using Mediator;

namespace VroksNet.Application.Publishers.ListDuePublishers;

/// <summary>The ids of the enabled publishers whose interval has passed at <see cref="Now"/> — for the background worker.</summary>
public sealed record ListDuePublishers(DateTimeOffset Now) : IRequest<IReadOnlyList<Guid>>;
