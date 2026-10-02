using Mediator;
using VroksNet.Application.Abstractions;

namespace VroksNet.Application.Publishers.SetPublisherEnabled;

public sealed class SetPublisherEnabledHandler(IPublisherRepository publishers) : IRequestHandler<SetPublisherEnabled, bool>
{
    public ValueTask<bool> Handle(SetPublisherEnabled request, CancellationToken cancellationToken)
        => new(publishers.SetEnabledAsync(request.Id, request.Enabled, cancellationToken));
}
