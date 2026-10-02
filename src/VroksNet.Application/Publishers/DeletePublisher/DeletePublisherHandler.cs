using Mediator;
using VroksNet.Application.Abstractions;

namespace VroksNet.Application.Publishers.DeletePublisher;

public sealed class DeletePublisherHandler(IPublisherRepository publishers) : IRequestHandler<DeletePublisher, bool>
{
    public ValueTask<bool> Handle(DeletePublisher request, CancellationToken cancellationToken)
        => new(publishers.DeleteAsync(request.Id, cancellationToken));
}
