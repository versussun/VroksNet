using Mediator;
using VroksNet.Application.Abstractions;
using VroksNet.Domain.Connections;

namespace VroksNet.Application.Connections.CreateConnection;

public sealed class CreateConnectionHandler(IConnectionRepository repository) : IRequestHandler<CreateConnection, Guid>
{
    public async ValueTask<Guid> Handle(CreateConnection request, CancellationToken cancellationToken)
    {
        var name = UniqueNames.Normalize(request.Name, "connection");
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Value);
        UniqueNames.EnsureFree((await repository.FindByNameAsync(name, cancellationToken))?.Id, null, "connection", name);

        var connection = new Connection
        {
            Id = Guid.NewGuid(),
            Name = name,
            ServiceType = request.ServiceType,
            Value = request.Value,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await repository.InsertAsync(connection, cancellationToken);

        return connection.Id;
    }
}
