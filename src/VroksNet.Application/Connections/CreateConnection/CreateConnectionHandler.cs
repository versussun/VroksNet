using Mediator;
using VroksNet.Application.Abstractions;
using VroksNet.Domain.Connections;

namespace VroksNet.Application.Connections.CreateConnection;

public sealed class CreateConnectionHandler(IConnectionRepository repository) : IRequestHandler<CreateConnection, Guid>
{
    public async ValueTask<Guid> Handle(CreateConnection request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Value);

        var connection = new Connection
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            ServiceType = request.ServiceType,
            Value = request.Value,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await repository.InsertAsync(connection, cancellationToken);

        return connection.Id;
    }
}
