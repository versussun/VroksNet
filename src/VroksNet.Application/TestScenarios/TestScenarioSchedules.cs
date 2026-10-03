using VroksNet.Application.Abstractions;

namespace VroksNet.Application.TestScenarios;

/// <summary>A scenario's schedule as create/update accept it (ADR 0002): normalized, and validated with a reason.</summary>
internal static class TestScenarioSchedules
{
    /// <summary>
    /// Trims both values and treats blank as "not set". A time zone without a schedule, an invalid
    /// expression or an unknown zone throws <see cref="ArgumentException"/> (the endpoints' 400).
    /// </summary>
    public static (string? Schedule, string? TimeZone) Normalize(ICronSchedule cron, string? schedule, string? timeZone)
    {
        var expression = string.IsNullOrWhiteSpace(schedule) ? null : string.Join(' ', schedule.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var zone = string.IsNullOrWhiteSpace(timeZone) ? null : timeZone.Trim();

        if (expression is null)
        {
            return zone is null ? (null, null) : throw new ArgumentException("A time zone needs a schedule; set the cron expression too, or clear the time zone.");
        }

        if (cron.Validate(expression, zone) is { } problem)
        {
            throw new ArgumentException(problem);
        }

        return (expression, zone);
    }
}
