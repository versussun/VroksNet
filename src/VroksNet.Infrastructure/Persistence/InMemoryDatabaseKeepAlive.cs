using Microsoft.Data.Sqlite;

namespace VroksNet.Infrastructure.Persistence;

/// <summary>
/// Holds one open connection to the shared-cache in-memory SQLite database alive for the app's
/// lifetime — see <c>InfrastructureServiceCollectionExtensions.AddInfrastructure</c>. A
/// shared-cache in-memory database only exists as long as at least one connection to it is open;
/// every <c>VroksNetDbContext</c> opens and closes its own short-lived connection via
/// <c>IDbContextFactory</c> (see .claude/CLAUDE.md "Infrastructure notes"), so without this the
/// database would be dropped the instant the first of those closes. Registered as a singleton and
/// resolved eagerly in <c>InitializeDatabaseAsync</c> — DI disposes it (dropping the database) on
/// app shutdown. Only registered when running in in-memory mode; unused (and undisposed-of-nothing)
/// against a file-backed database.
/// </summary>
public sealed class InMemoryDatabaseKeepAlive(SqliteConnection connection) : IDisposable
{
    public void Dispose() => connection.Dispose();
}
