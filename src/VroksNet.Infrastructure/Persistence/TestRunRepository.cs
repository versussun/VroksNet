using Microsoft.EntityFrameworkCore;
using VroksNet.Application.Abstractions;
using VroksNet.Domain.TestRuns;

namespace VroksNet.Infrastructure.Persistence;

/// <summary>
/// <see cref="TestRun"/> persistence. Every state change is a single conditional UPDATE through
/// the write queue (<c>WHERE Status = expected</c>), so concurrent cancel/start/complete calls
/// can't overwrite each other: whichever applies first wins, and the rest report false.
/// </summary>
public sealed class TestRunRepository(
    IDbContextFactory<VroksNetDbContext> contextFactory,
    IDbWriteQueue writeQueue) : ITestRunRepository
{
    private static readonly TestRunStatus[] FinishedStatuses =
        [TestRunStatus.Passed, TestRunStatus.Failed, TestRunStatus.Cancelled, TestRunStatus.Interrupted];

    public Task InsertAsync(TestRun run, CancellationToken cancellationToken)
    {
        return writeQueue.EnqueueAsync(async (context, ct) =>
        {
            context.TestRuns.Add(run);
            await context.SaveChangesAsync(ct);
        }, cancellationToken);
    }

    public async Task<TestRun?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.TestRuns.AsNoTracking().FirstOrDefaultAsync(run => run.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<TestRun>> ListAsync(TestRunFilter filter, TestRunCursor? after, int limit, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.TestRuns.AsNoTracking();

        if (filter.TestScenarioId is { } testScenarioId)
        {
            query = query.Where(run => run.TestScenarioId == testScenarioId);
        }

        if (filter.Status is { } status)
        {
            query = query.Where(run => run.Status == status);
        }

        if (after is not null)
        {
            query = query.Where(run => run.ScheduledFor < after.ScheduledFor
                || (run.ScheduledFor == after.ScheduledFor && run.Id.CompareTo(after.Id) < 0));
        }

        return await query
            .OrderByDescending(run => run.ScheduledFor)
            .ThenByDescending(run => run.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TestRun>> ListDueAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.TestRuns.AsNoTracking()
            .Where(run => run.Status == TestRunStatus.Queued && run.ScheduledFor <= now)
            .OrderBy(run => run.ScheduledFor)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> MarkRunningAsync(Guid id, DateTimeOffset startedAt, CancellationToken cancellationToken)
    {
        var updated = 0;
        await writeQueue.EnqueueAsync(async (context, ct) =>
        {
            updated = await context.TestRuns
                .Where(run => run.Id == id && run.Status == TestRunStatus.Queued)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(run => run.Status, TestRunStatus.Running)
                    .SetProperty(run => run.StartedAt, startedAt), ct);
        }, cancellationToken);

        return updated > 0;
    }

    public async Task<bool> CompleteAsync(Guid id, TestRunOutcome outcome, CancellationToken cancellationToken)
    {
        var updated = 0;
        await writeQueue.EnqueueAsync(async (context, ct) =>
        {
            updated = await context.TestRuns
                .Where(run => run.Id == id && run.Status == TestRunStatus.Running)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(run => run.Status, outcome.Status)
                    .SetProperty(run => run.FinishedAt, outcome.FinishedAt)
                    .SetProperty(run => run.Message, outcome.Message)
                    .SetProperty(run => run.StatusCode, outcome.StatusCode)
                    .SetProperty(run => run.ContractValid, outcome.ContractValid)
                    .SetProperty(run => run.ValidationErrors, outcome.ValidationErrors), ct);
        }, cancellationToken);

        return updated > 0;
    }

    public async Task<bool> CancelQueuedAsync(Guid id, DateTimeOffset cancelledAt, CancellationToken cancellationToken)
    {
        var updated = 0;
        await writeQueue.EnqueueAsync(async (context, ct) =>
        {
            updated = await context.TestRuns
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
            updated = await context.TestRuns
                .Where(run => run.Status == TestRunStatus.Running)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(run => run.Status, TestRunStatus.Interrupted)
                    .SetProperty(run => run.FinishedAt, at)
                    .SetProperty(run => run.Message, message), ct);
        }, cancellationToken);

        return updated;
    }

    public async Task<int> PruneAsync(int keepPerScenario, CancellationToken cancellationToken)
    {
        var deleted = 0;
        await writeQueue.EnqueueAsync(async (context, ct) =>
        {
            // Only scenarios over the limit; there are few of them and few runs each, so a query
            // per scenario is fine. Queued and running runs are never deleted.
            var scenarioIds = await context.TestRuns
                .Where(run => FinishedStatuses.Contains(run.Status))
                .GroupBy(run => run.TestScenarioId)
                .Where(group => group.Count() > keepPerScenario)
                .Select(group => group.Key)
                .ToListAsync(ct);

            foreach (var scenarioId in scenarioIds)
            {
                var stale = await context.TestRuns
                    .Where(run => run.TestScenarioId == scenarioId && FinishedStatuses.Contains(run.Status))
                    .OrderByDescending(run => run.ScheduledFor)
                    .ThenByDescending(run => run.Id)
                    .Skip(keepPerScenario)
                    .Select(run => run.Id)
                    .ToListAsync(ct);

                deleted += await context.TestRuns.Where(run => stale.Contains(run.Id)).ExecuteDeleteAsync(ct);
            }
        }, cancellationToken);

        return deleted;
    }
}
