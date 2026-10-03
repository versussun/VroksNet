using Mediator;
using VroksNet.Application.Abstractions;

namespace VroksNet.Application.Connections.ListConnections;

public sealed class ListConnectionsHandler(IConnectionRepository repository)
    : IRequestHandler<ListConnections, IReadOnlyList<ConnectionSummary>>
{
    public async ValueTask<IReadOnlyList<ConnectionSummary>> Handle(ListConnections request, CancellationToken cancellationToken)
    {
        var connections = await repository.ListAsync(cancellationToken);

        return connections
            .OrderBy(connection => connection.Name, StringComparer.Ordinal)
            .Select(connection => new ConnectionSummary(
                connection.Id,
                connection.Name,
                connection.ServiceType,
                connection.Value,
                connection.CreatedAt,
                connection.UpdatedAt,
                connection.ProvisionedAt))
            .ToList();
    }
}
