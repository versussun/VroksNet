using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using VroksNet.Domain.ApiSpecifications;
using VroksNet.Domain.MockEndpoints;
using VroksNet.Infrastructure.Persistence;

namespace VroksNet.UnitTests.Persistence;

/// <summary>
/// Runs against a real (temp-file) SQLite database through the actual write queue + background
/// consumer — regression coverage for the replace-by-title upsert, which previously threw
/// <see cref="DbUpdateConcurrencyException"/> when implemented as attach-and-patch instead of
/// delete-and-insert (see .claude/CLAUDE.md).
/// </summary>
public sealed class ApiSpecificationRepositoryTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"vroksnet-test-{Guid.NewGuid():N}.db");
    private TestDbContextFactory _contextFactory = null!;
    private DbWriteQueue _writeQueue = null!;
    private DbWriteBackgroundService _writeService = null!;
    private ApiSpecificationRepository _repository = null!;

    public async ValueTask InitializeAsync()
    {
        _contextFactory = new TestDbContextFactory($"Data Source={_dbPath}");
        await using (var context = await _contextFactory.CreateDbContextAsync())
        {
            await context.Database.EnsureCreatedAsync();
        }

        _writeQueue = new DbWriteQueue();
        _writeService = new DbWriteBackgroundService(_writeQueue, _contextFactory, NullLogger<DbWriteBackgroundService>.Instance);
        await _writeService.StartAsync(CancellationToken.None);

        _repository = new ApiSpecificationRepository(_contextFactory, _writeQueue);
    }

    public async ValueTask DisposeAsync()
    {
        await _writeService.StopAsync(CancellationToken.None);
        _writeService.Dispose();

        // Microsoft.Data.Sqlite pools the native connection by default, so it can still hold
        // the file open here even though every DbContext using it has been disposed.
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }

    [Fact]
    public async Task UpsertAsync_NewTitle_IsFoundAfterwards()
    {
        var specification = CreateSpecification("Orders API", "GET /orders");

        await _repository.UpsertAsync(specification, TestContext.Current.CancellationToken);

        var stored = await _repository.FindByTitleAsync("Orders API", TestContext.Current.CancellationToken);
        Assert.NotNull(stored);
        Assert.Equal(specification.Id, stored.Id);
        Assert.Single(stored.Endpoints);
    }

    [Fact]
    public async Task UpsertAsync_SameTitleTwice_ReplacesWithoutThrowing()
    {
        var first = CreateSpecification("Orders API", "GET /orders");
        await _repository.UpsertAsync(first, TestContext.Current.CancellationToken);

        var second = CreateSpecification("Orders API", "GET /orders", "POST /orders", "DELETE /orders/{id}");
        await _repository.UpsertAsync(second, TestContext.Current.CancellationToken);

        var all = await _repository.ListAsync(TestContext.Current.CancellationToken);
        var stored = Assert.Single(all);
        Assert.Equal(3, stored.Endpoints.Count);
    }

    private static ApiSpecification CreateSpecification(string title, params string[] operationKeys)
    {
        var specification = new ApiSpecification
        {
            Id = Guid.NewGuid(),
            Title = title,
            Kind = SpecificationKind.OpenApi,
            RawContent = "raw",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        specification.Endpoints = operationKeys
            .Select(key => new MockEndpoint { Id = Guid.NewGuid(), SpecificationId = specification.Id, OperationKey = key })
            .ToList();

        return specification;
    }

    private sealed class TestDbContextFactory(string connectionString) : IDbContextFactory<VroksNetDbContext>
    {
        public VroksNetDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<VroksNetDbContext>().UseSqlite(connectionString).Options;
            return new VroksNetDbContext(options);
        }

        public Task<VroksNetDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }
}
