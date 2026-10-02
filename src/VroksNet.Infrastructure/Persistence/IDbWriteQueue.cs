namespace VroksNet.Infrastructure.Persistence;

/// <summary>
/// Serializes all database writes through a single background consumer, so the Mock API and the
/// async publish worker never write to SQLite concurrently — see docs/project-brief.md section 3
/// "Storage: decision" on the concurrent-write risk this avoids. Reads don't go through this
/// (SQLite WAL mode allows reading while a write is in progress).
/// </summary>
public interface IDbWriteQueue
{
    /// <summary>Enqueues a write and returns a task that completes once it has actually run (and been serialized against every other queued write).</summary>
    Task EnqueueAsync(Func<VroksNetDbContext, CancellationToken, Task> operation, CancellationToken cancellationToken = default);
}
