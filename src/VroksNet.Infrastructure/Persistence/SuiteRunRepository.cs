using Microsoft.EntityFrameworkCore;
using VroksNet.Application.Abstractions;
using VroksNet.Domain.TestRuns;
using VroksNet.Domain.TestSuites;

namespace VroksNet.Infrastructure.Persistence;

/// <summary>
/// <see cref="SuiteRun"/> persistence, with the same conditional single-UPDATE state changes as
/// <see cref="TestRunRepository"/> — whichever of a racing cancel/start/complete applies first wins.
/// </summary>
public sealed class SuiteRunRepository(
    IDbContextFactory<VroksNetDbContext> contextFactory,
    IDbWriteQueue writeQueue) : ISuiteRunRepository
{
    private static readonly TestRunStatus[] FinishedStatuses =
        [TestRunStatus.Passed, TestRunStatus.Failed, TestRunStatus.Cancelled, TestRunStatus.Interrupted];

    public Task InsertAsync(SuiteRun run, CancellationToken cancellationToken)
    {
        return writeQueue.EnqueueAsync(async (context, ct) =>
        {
            context.SuiteRuns.Add(run);
            await context.SaveChangesAsync(ct);
        }, cancellationToken);
    }

    public async Task<SuiteRun?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.SuiteRuns.AsNoTracking().FirstOrDefaultAsync(run => run.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<SuiteRun>> ListAsync(Guid testSuiteId, int limit, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.SuiteRuns.AsNoTracking()
            .Where(run => run.TestSuiteId == testSuiteId)
            .OrderByDescending(run => run.ScheduledFor)
            .ThenByDescending(run => run.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SuiteRun>> ListDueAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.SuiteRuns.AsNoTracking()
            .Where(run => run.Status == TestRunStatus.Queued && run.ScheduledFor <= now)
            .OrderBy(run => run.ScheduledFor)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> MarkRunningAsync(Guid id, DateTimeOffset startedAt, CancellationToken cancellationToken)
    {
        var updated = 0;
        await writeQueue.EnqueueAsync(async (context, ct) =>
        {
            updated = await context.SuiteRuns
                .Where(run => run.Id == id && run.Status == TestRunStatus.Queued)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(run => run.Status, TestRunStatus.Running)
                    .SetProperty(run => run.StartedAt, startedAt), ct);
        }, cancellationToken);

        return updated > 0;
    }

    public async Task<bool> CompleteAsync(Guid id, TestRunStatus status, DateTimeOffset finishedAt, string message, CancellationToken cancellationToken)
    {
        var updated = 0;
        await writeQueue.EnqueueAsync(async (context, ct) =>
        {
            updated = await context.SuiteRuns
                .Where(run => run.Id == id && run.Status == TestRunStatus.Running)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(run => run.Status, status)
                    .SetProperty(run => run.FinishedAt, finishedAt)
                    .SetProperty(run => run.Message, message), ct);
        }, cancellationToken);

        return updated > 0;
    }

    public async Task<bool> CancelQueuedAsync(Guid id, DateTimeOffset cancelledAt, CancellationToken cancellationToken)
    {
        var updated = 0;
        await writeQueue.EnqueueAsync(async (context, ct) =>
        {
            updated = await context.SuiteRuns
                .Where(run => run.Id == id && run.Status == TestRunStatus.Queued)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(run => run.Status, TestRunStatus.Cancelled)
                    .SetProperty(run => run.FinishedAt, cancelledAt)
                    .SetProperty(run => run.Message, "Cancelled before it started."), ct);
        }, cancellationToken);

        return updated > 0;
    }

    public async Task<int> InterruptRunningAsync(DateTimeOffset at, string message, CancellationToken cancellationToken)
    {
        var updated = 0;
        await writeQueue.EnqueueAsync(async (context, ct) =>
        {
            updated = await context.SuiteRuns
                .Where(run => run.Status == TestRunStatus.Running)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(run => run.Status, TestRunStatus.Interrupted)
                    .SetProperty(run => run.FinishedAt, at)
                    .SetProperty(run => run.Message, message), ct);
        }, cancellationToken);

        return updated;
    }

    public async Task<int> PruneAsync(int keepPerSuite, CancellationToken cancellationToken)
    {
        var deleted = 0;
        await writeQueue.EnqueueAsync(async (context, ct) =>
        {
            var suiteIds = await context.SuiteRuns
                .Where(run => FinishedStatuses.Contains(run.Status))
                .GroupBy(run => run.TestSuiteId)
                .Where(group => group.Count() > keepPerSuite)
                .Select(group => group.Key)
                .ToListAsync(ct);

            foreach (var suiteId in suiteIds)
            {
                var stale = await context.SuiteRuns
                    .Where(run => run.TestSuiteId == suiteId && FinishedStatuses.Contains(run.Status))
                    .OrderByDescending(run => run.ScheduledFor)
                    .ThenByDescending(run => run.Id)
                    .Skip(keepPerSuite)
                    .Select(run => run.Id)
                    .ToListAsync(ct);

                deleted += await context.SuiteRuns.Where(run => stale.Contains(run.Id)).ExecuteDeleteAsync(ct);
            }
        }, cancellationToken);

        return deleted;
    }
}
