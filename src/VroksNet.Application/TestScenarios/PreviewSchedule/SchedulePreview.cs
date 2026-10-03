namespace VroksNet.Application.TestScenarios.PreviewSchedule;

/// <summary><see cref="Error"/> is the reason the schedule can't be saved, null when it can; <see cref="NextRuns"/> are in UTC and empty when there's no schedule or it's invalid.</summary>
public sealed record SchedulePreview(string? Error, IReadOnlyList<DateTimeOffset> NextRuns);
