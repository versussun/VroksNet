using Mediator;
using VroksNet.Application.Abstractions;

namespace VroksNet.Application.Connections.DeleteConnection;

public sealed class DeleteConnectionHandler(IConnectionRepository repository) : IRequestHandler<DeleteConnection, bool>
{
    public ValueTask<bool> Handle(DeleteConnection request, CancellationToken cancellationToken)
        => new(repository.DeleteAsync(request.Id, cancellationToken));
}
