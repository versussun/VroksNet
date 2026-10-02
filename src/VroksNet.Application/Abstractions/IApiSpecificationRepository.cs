using VroksNet.Domain.ApiSpecifications;

namespace VroksNet.Application.Abstractions;

public interface IApiSpecificationRepository
{
    Task<IReadOnlyList<ApiSpecification>> ListAsync(CancellationToken cancellationToken);

    Task<ApiSpecification?> FindByTitleAsync(string title, CancellationToken cancellationToken);

    Task<ApiSpecification?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Sets <see cref="Domain.MockEndpoints.MockEndpoint.ServeAtRealPath"/> on the given endpoints; ids that don't exist are ignored.</summary>
    /// <returns>How many endpoints were updated — fewer than asked means some no longer exist (e.g. a concurrent re-import replaced them).</returns>
    Task<int> SetServeAtRealPathAsync(IReadOnlyCollection<Guid> mockEndpointIds, bool serveAtRealPath, CancellationToken cancellationToken);

    /// <summary>Sets <see cref="Domain.MockEndpoints.MockEndpoint.IsEnabled"/> on the given endpoints; ids that don't exist are ignored.</summary>
    /// <returns>How many endpoints were updated — fewer than asked means some no longer exist.</returns>
    Task<int> SetEnabledAsync(IReadOnlyCollection<Guid> mockEndpointIds, bool enabled, CancellationToken cancellationToken);

    /// <summary>
    /// Inserts <paramref name="imported"/>, or — when a specification with the same
    /// <see cref="ApiSpecification.Title"/> exists — updates that one in place through
    /// <see cref="ApiSpecification.ApplyReimport"/>: kept operations keep their ids and admin-set
    /// state, and storing an unchanged import writes nothing.
    /// </summary>
    /// <returns>The stored specification's id: the existing one's when it was updated, otherwise <paramref name="imported"/>'s.</returns>
    Task<Guid> UpsertAsync(ApiSpecification imported, CancellationToken cancellationToken);
}
