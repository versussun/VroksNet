using VroksNet.Domain.Connections;

namespace VroksNet.Application.Abstractions;

public interface IConnectionRepository
{
    Task<IReadOnlyList<Connection>> ListAsync(CancellationToken cancellationToken);

    Task<Connection?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The one with exactly this name (names are unique, compared as stored — trimmed, case-sensitive); null if none.</summary>
    Task<Connection?> FindByNameAsync(string name, CancellationToken cancellationToken);

    Task InsertAsync(Connection connection, CancellationToken cancellationToken);

    /// <returns>False if no connection with <see cref="Connection.Id"/> exists.</returns>
    Task<bool> UpdateAsync(Connection connection, CancellationToken cancellationToken);

    /// <returns>False if no connection with that id exists.</returns>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}
