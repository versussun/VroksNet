using Mediator;
using VroksNet.Application.Abstractions;

namespace VroksNet.Application.Connections.UpdateConnection;

public sealed class UpdateConnectionHandler(IConnectionRepository repository) : IRequestHandler<UpdateConnection, bool>
{
    public async ValueTask<bool> Handle(UpdateConnection request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Value);

        var connection = await repository.FindByIdAsync(request.Id, cancellationToken);
        if (connection is null)
        {
            return false;
        }

        connection.Name = request.Name;
        connection.ServiceType = request.ServiceType;
        connection.Value = request.Value;
        connection.UpdatedAt = DateTimeOffset.UtcNow;

        return await repository.UpdateAsync(connection, cancellationToken);
    }
}
