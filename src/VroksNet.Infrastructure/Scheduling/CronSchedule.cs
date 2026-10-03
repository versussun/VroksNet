using Cronos;
using VroksNet.Application.Abstractions;

namespace VroksNet.Infrastructure.Scheduling;

/// <summary>
/// <see cref="ICronSchedule"/> on Cronos. Across a daylight-saving transition Cronos runs a time
/// that's skipped (spring forward) at the end of the gap, and a time that repeats (fall back) once,
/// except for interval expressions like <c>*/10 * * * *</c>, which keep their pace through both.
/// </summary>
public sealed class CronSchedule : ICronSchedule
{
    public string? Validate(string expression, string? timeZone)
    {
        CronExpression cron;
        try
        {
            cron = CronExpression.Parse(expression, CronFormat.Standard);
        }
        catch (CronFormatException ex)
        {
            return $"\"{expression}\" isn't a valid cron expression: {ex.Message} Use 5 fields: minute hour day-of-month month day-of-week, e.g. \"0 9 * * 1-5\".";
        }

        if (!TryFindZone(timeZone, out var zone))
        {
            return $"\"{timeZone}\" isn't a known time zone. Use an IANA name, e.g. \"Europe/Kyiv\" or \"UTC\".";
        }

        return cron.GetNextOccurrence(DateTimeOffset.UtcNow, zone) is null
            ? $"\"{expression}\" never occurs."
            : null;
    }

    public DateTimeOffset? GetNextOccurrence(string expression, string? timeZone, DateTimeOffset from, bool inclusive = false)
    {
        if (!TryFindZone(timeZone, out var zone) || !CronExpression.TryParse(expression, CronFormat.Standard, out var cron))
        {
            return null;
        }

        return cron.GetNextOccurrence(from, zone, inclusive)?.ToUniversalTime();
    }

    private static bool TryFindZone(string? timeZone, out TimeZoneInfo zone)
    {
        if (string.IsNullOrEmpty(timeZone))
        {
            zone = TimeZoneInfo.Utc;
            return true;
        }

        if (TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out var found))
        {
            zone = found;
            return true;
        }

        zone = TimeZoneInfo.Utc;
        return false;
    }
}
