using Mediator;
using VroksNet.Application.Abstractions;

namespace VroksNet.Application.TestRuns.ListTestRuns;

/// <summary>Throws <see cref="ArgumentException"/> for a cursor this API didn't hand out.</summary>
public sealed class ListTestRunsHandler(ITestRunRepository runs) : IRequestHandler<ListTestRuns, TestRunPage>
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 200;

    public async ValueTask<TestRunPage> Handle(ListTestRuns request, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(request.Limit ?? DefaultLimit, 1, MaxLimit);
        var after = request.Cursor is null ? null : TestRunCursorFormat.Parse(request.Cursor);

        // One extra row tells whether an older page exists without a separate count query.
        var rows = await runs.ListAsync(new TestRunFilter(request.TestScenarioId, request.Status), after, limit + 1, cancellationToken);
        var page = rows.Take(limit).ToList();
        var nextCursor = rows.Count > limit ? TestRunCursorFormat.Format(new TestRunCursor(page[^1].ScheduledFor, page[^1].Id)) : null;

        return new TestRunPage(page.Select(TestRunSummaries.From).ToList(), nextCursor);
    }
}
