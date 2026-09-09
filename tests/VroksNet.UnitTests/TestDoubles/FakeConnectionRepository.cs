using VroksNet.Application.Abstractions;
using VroksNet.Domain.Connections;

namespace VroksNet.UnitTests.TestDoubles;

/// <summary>In-memory stand-in for <see cref="IConnectionRepository"/>.</summary>
internal sealed class FakeConnectionRepository : IConnectionRepository
{
    private readonly List<Connection> _connections = [];

    public Task<IReadOnlyList<Connection>> ListAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Connection>>(_connections.ToList());

    public Task<Connection?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
        => Task.FromResult(_connections.FirstOrDefault(c => c.Id == id));

    public Task InsertAsync(Connection connection, CancellationToken cancellationToken)
    {
        _connections.Add(connection);
        return Task.CompletedTask;
    }

    public Task<bool> UpdateAsync(Connection connection, CancellationToken cancellationToken)
    {
        var index = _connections.FindIndex(c => c.Id == connection.Id);
        if (index < 0)
        {
            return Task.FromResult(false);
        }

        _connections[index] = connection;
        return Task.FromResult(true);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
        => Task.FromResult(_connections.RemoveAll(c => c.Id == id) > 0);
}
