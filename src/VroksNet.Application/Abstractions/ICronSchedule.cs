namespace VroksNet.Application.Abstractions;

/// <summary>
/// Cron arithmetic for scheduled test runs (ADR 0002): standard 5-field expressions (minute, hour,
/// day of month, month, day of week) read in an IANA time zone, with that zone's daylight-saving
/// rules. A null zone means UTC. Implemented in Infrastructure, so no parser library leaks inward.
/// </summary>
public interface ICronSchedule
{
    /// <summary>Why <paramref name="expression"/> in <paramref name="timeZone"/> can't be used — null when it can.</summary>
    string? Validate(string expression, string? timeZone);

    /// <summary>
    /// The first occurrence after <paramref name="from"/> (or at it, when <paramref name="inclusive"/>),
    /// in UTC; null if there's none, or if the expression or zone isn't valid (see <see cref="Validate"/>).
    /// </summary>
    DateTimeOffset? GetNextOccurrence(string expression, string? timeZone, DateTimeOffset from, bool inclusive = false);
}
