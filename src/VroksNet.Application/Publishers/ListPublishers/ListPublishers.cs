using Mediator;

namespace VroksNet.Application.Publishers.ListPublishers;

public sealed record ListPublishers : IRequest<IReadOnlyList<PublisherSummary>>;
