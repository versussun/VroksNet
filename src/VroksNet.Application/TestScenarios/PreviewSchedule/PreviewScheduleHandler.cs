using Mediator;
using VroksNet.Application.Abstractions;

namespace VroksNet.Application.TestScenarios.PreviewSchedule;

public sealed class PreviewScheduleHandler(ICronSchedule cron, TimeProvider timeProvider) : IRequestHandler<PreviewSchedule, SchedulePreview>
{
    private const int MaxCount = 20;

    public ValueTask<SchedulePreview> Handle(PreviewSchedule request, CancellationToken cancellationToken)
    {
        string? schedule;
        string? timeZone;
        try
        {
            (schedule, timeZone) = TestScenarioSchedules.Normalize(cron, request.Schedule, request.TimeZone);
        }
        catch (ArgumentException ex)
        {
            return new(new SchedulePreview(ex.Message, []));
        }

        var nextRuns = new List<DateTimeOffset>();
        var from = timeProvider.GetUtcNow();
        while (schedule is not null && nextRuns.Count < Math.Clamp(request.Count, 1, MaxCount)
            && cron.GetNextOccurrence(schedule, timeZone, from) is { } next)
        {
            nextRuns.Add(next);
            from = next;
        }

        return new(new SchedulePreview(null, nextRuns));
    }
}
