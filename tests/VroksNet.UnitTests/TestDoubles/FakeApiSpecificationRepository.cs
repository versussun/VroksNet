using VroksNet.Application.Abstractions;
using VroksNet.Domain.ApiSpecifications;

namespace VroksNet.UnitTests.TestDoubles;

/// <summary>In-memory stand-in for <see cref="IApiSpecificationRepository"/> — mirrors the real
/// implementation's replace-by-title semantics without touching a database.</summary>
internal sealed class FakeApiSpecificationRepository : IApiSpecificationRepository
{
    private readonly List<ApiSpecification> _specifications = [];

    public Task<IReadOnlyList<ApiSpecification>> ListAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<ApiSpecification>>(_specifications.ToList());

    public Task<ApiSpecification?> FindByTitleAsync(string title, CancellationToken cancellationToken)
        => Task.FromResult(_specifications.FirstOrDefault(s => s.Title == title));

    public Task<ApiSpecification?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
        => Task.FromResult(_specifications.FirstOrDefault(s => s.Id == id));

    /// <summary>When true, <see cref="SetServeAtRealPathAsync"/>/<see cref="SetEnabledAsync"/> update nothing — as if a concurrent re-import had replaced the endpoints.</summary>
    public bool SimulateConcurrentReimport { get; set; }

    public Task<int> SetEnabledAsync(IReadOnlyCollection<Guid> mockEndpointIds, bool enabled, CancellationToken cancellationToken)
    {
        if (SimulateConcurrentReimport)
        {
            return Task.FromResult(0);
        }

        var endpoints = _specifications.SelectMany(s => s.Endpoints).Where(e => mockEndpointIds.Contains(e.Id)).ToList();
        foreach (var endpoint in endpoints)
        {
            endpoint.IsEnabled = enabled;
        }

        return Task.FromResult(endpoints.Count);
    }

    public Task<int> SetServeAtRealPathAsync(IReadOnlyCollection<Guid> mockEndpointIds, bool serveAtRealPath, CancellationToken cancellationToken)
    {
        if (SimulateConcurrentReimport)
        {
            return Task.FromResult(0);
        }

        var endpoints = _specifications.SelectMany(s => s.Endpoints).Where(e => mockEndpointIds.Contains(e.Id)).ToList();
        foreach (var endpoint in endpoints)
        {
            endpoint.ServeAtRealPath = serveAtRealPath;
        }

        return Task.FromResult(endpoints.Count);
    }

    public Task UpsertAsync(ApiSpecification specification, CancellationToken cancellationToken)
    {
        _specifications.RemoveAll(s => s.Title == specification.Title);
        _specifications.Add(specification);
        return Task.CompletedTask;
    }
}
