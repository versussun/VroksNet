using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VroksNet.Domain.ApiSpecifications;
using VroksNet.Infrastructure.Persistence;

namespace VroksNet.UnitTests.Persistence;

/// <summary>
/// Exercises the exact "named + shared-cache in-memory SQLite database, kept alive by one
/// dedicated connection" technique <c>InfrastructureServiceCollectionExtensions.AddInfrastructure</c>
/// uses for its no-connection-string-configured default (see .claude/CLAUDE.md "Infrastructure
/// notes") — not through the full DI pipeline, but at the same level
/// <see cref="InMemoryDatabaseKeepAlive"/> operates at, since that's where getting this wrong
/// would actually bite (data silently vanishing between requests).
/// </summary>
public sealed class InMemoryDatabaseTests
{
    [Fact]
    public async Task SharedCacheDatabase_PersistsAcrossSeparateDbContexts_WhileKeptAlive()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = UniqueSharedCacheConnectionString();

        // Mirrors InMemoryDatabaseKeepAlive — without this open connection, the database would be
        // dropped the moment the first context below closes its own.
        await using var keepAliveConnection = new SqliteConnection(connectionString);
        await keepAliveConnection.OpenAsync(cancellationToken);

        await using (var writingContext = CreateContext(connectionString))
        {
            await writingContext.Database.EnsureCreatedAsync(cancellationToken);
            writingContext.ApiSpecifications.Add(new ApiSpecification
            {
                Id = Guid.NewGuid(),
                Title = "Orders API",
                Kind = SpecificationKind.OpenApi,
                RawContent = "raw",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await writingContext.SaveChangesAsync(cancellationToken);
        }

        // A brand-new context — its own separate SqliteConnection, exactly like the next request
        // IDbContextFactory would hand out — proves the data lives in the shared cache, not just
        // in the first context's own connection.
        await using var readingContext = CreateContext(connectionString);
        var stored = await readingContext.ApiSpecifications.SingleOrDefaultAsync(s => s.Title == "Orders API", cancellationToken);

        Assert.NotNull(stored);
    }

    [Fact]
    public async Task SharedCacheDatabase_IsDroppedOnceItsLastConnectionCloses()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = UniqueSharedCacheConnectionString();

        await using (var keepAliveConnection = new SqliteConnection(connectionString))
        {
            await keepAliveConnection.OpenAsync(cancellationToken);

            await using var context = CreateContext(connectionString);
            await context.Database.EnsureCreatedAsync(cancellationToken);
        }
        // keepAliveConnection closed here — this is exactly the bug InMemoryDatabaseKeepAlive
        // exists to prevent: with nothing holding a connection open, the database (and its
        // schema) is gone.

        await using var contextAfterClose = CreateContext(connectionString);
        await Assert.ThrowsAsync<SqliteException>(
            () => contextAfterClose.ApiSpecifications.ToListAsync(cancellationToken));
    }

    private static string UniqueSharedCacheConnectionString() => $"Data Source=InMemoryDatabaseTests-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";

    private static VroksNetDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<VroksNetDbContext>().UseSqlite(connectionString).Options);
}
