using VroksNet.Domain.ApiSpecifications;

namespace VroksNet.Application.Abstractions;

public interface IApiSpecificationRepository
{
    Task<ApiSpecification?> FindByTitleAsync(string title, CancellationToken cancellationToken);

    /// <summary>Inserts a new specification, or replaces the existing one with the same <see cref="ApiSpecification.Title"/>.</summary>
    Task UpsertAsync(ApiSpecification specification, CancellationToken cancellationToken);
}
