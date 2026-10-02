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

    /// <summary>Inserts a new specification, or replaces the existing one with the same <see cref="ApiSpecification.Title"/>.</summary>
    Task UpsertAsync(ApiSpecification specification, CancellationToken cancellationToken);
}
