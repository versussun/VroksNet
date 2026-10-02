using Mediator;
using VroksNet.Application.Abstractions;
using VroksNet.Domain.Publishers;

namespace VroksNet.Application.Publishers.ListDuePublishers;

public sealed class ListDuePublishersHandler(IPublisherRepository publishers) : IRequestHandler<ListDuePublishers, IReadOnlyList<Guid>>
{
    public async ValueTask<IReadOnlyList<Guid>> Handle(ListDuePublishers request, CancellationToken cancellationToken)
    {
        var all = await publishers.ListAsync(cancellationToken);
        return all.Where(publisher => PublisherSchedule.IsDue(publisher, request.Now)).Select(publisher => publisher.Id).ToList();
    }
}
