using System.Collections.Concurrent;

namespace VroksNet.Application.TestRuns;

/// <summary>
/// The cancellation tokens of the runs executing in this process, so <c>CancelTestRun</c> can stop
/// a running one. A singleton; in memory is enough because there's one process (ADR 0002 — moving
/// it to the database is only needed if the app is ever scaled out). It also remembers which runs
/// were cancelled on purpose, so a cancelled run can be told apart from one cut off by shutdown.
/// </summary>
public sealed class TestRunCancellations
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _running = new();
    private readonly ConcurrentDictionary<Guid, bool> _cancelled = new();

    /// <summary>Tracks a run that's starting; its token fires on <see cref="Cancel"/> or when <paramref name="outer"/> does.</summary>
    public CancellationToken Register(Guid runId, CancellationToken outer)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(outer);
        _running[runId] = source;
        return source.Token;
    }

    /// <returns>False if the run isn't executing in this process.</returns>
    public bool Cancel(Guid runId)
    {
        if (!_running.TryGetValue(runId, out var source))
        {
            return false;
        }

        _cancelled[runId] = true;
        try
        {
            source.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Finished between the lookup and the cancel.
        }

        return true;
    }

    public bool WasCancelled(Guid runId) => _cancelled.ContainsKey(runId);

    /// <summary>Stops tracking a finished run.</summary>
    public void Unregister(Guid runId)
    {
        if (_running.TryRemove(runId, out var source))
        {
            source.Dispose();
        }

        _cancelled.TryRemove(runId, out _);
    }
}
