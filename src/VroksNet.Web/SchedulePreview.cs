namespace VroksNet.Web;

/// <summary>A schedule's next run times (UTC), or why it can't be saved.</summary>
public sealed record SchedulePreview(string? Error, DateTimeOffset[] NextRuns);
