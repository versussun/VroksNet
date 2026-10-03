using VroksNet.Infrastructure.Scheduling;

namespace VroksNet.UnitTests.Scheduling;

/// <summary>
/// The real Cronos-backed <see cref="CronSchedule"/>: validation reasons, time zones, and
/// daylight-saving transitions (ADR 0002). Europe/Kyiv in 2026: clocks go 03:00 → 04:00 on
/// 29 March (UTC+2 → UTC+3) and 04:00 → 03:00 on 25 October (UTC+3 → UTC+2).
/// </summary>
public sealed class CronScheduleTests
{
    private const string Kyiv = "Europe/Kyiv";
    private readonly CronSchedule _cron = new();

    [Theory]
    [InlineData("*/1 * * * *", null)]
    [InlineData("0 9 * * 1-5", Kyiv)]
    [InlineData("30 3 * * *", "UTC")]
    public void Validate_AcceptsStandardExpressionsAndIanaZones(string expression, string? timeZone)
        => Assert.Null(_cron.Validate(expression, timeZone));

    [Theory]
    [InlineData("not cron", null, "isn't a valid cron expression")]
    [InlineData("0 0 9 * * 1-5", null, "isn't a valid cron expression")] // 6 fields: seconds aren't supported
    [InlineData("61 * * * *", null, "isn't a valid cron expression")]
    [InlineData("0 9 * * *", "Mars/Olympus", "isn't a known time zone")]
    [InlineData("0 0 30 2 *", null, "never occurs")]
    public void Validate_SaysWhatsWrong(string expression, string? timeZone, string reason)
        => Assert.Contains(reason, _cron.Validate(expression, timeZone));

    [Fact]
    public void Weekdays_At9_InKyiv_FollowTheZonesOffset()
    {
        // Summer (UTC+3) and winter (UTC+2): the same wall-clock time, different UTC.
        Assert.Equal(At("2026-07-01T06:00Z"), _cron.GetNextOccurrence("0 9 * * 1-5", Kyiv, At("2026-07-01T00:00Z")));
        Assert.Equal(At("2026-12-01T07:00Z"), _cron.GetNextOccurrence("0 9 * * 1-5", Kyiv, At("2026-12-01T00:00Z")));
        // Friday evening → Monday.
        Assert.Equal(At("2026-07-06T06:00Z"), _cron.GetNextOccurrence("0 9 * * 1-5", Kyiv, At("2026-07-03T12:00Z")));
    }

    [Fact]
    public void SpringForward_InKyiv_ATimeInTheGapRunsAtItsEnd_Once()
    {
        // 03:30 doesn't exist on 29 March: it runs at 04:00 EEST (01:00Z), then at 03:30 the next day.
        var first = _cron.GetNextOccurrence("30 3 * * *", Kyiv, At("2026-03-28T12:00Z"));
        Assert.Equal(At("2026-03-29T01:00Z"), first);
        Assert.Equal(At("2026-03-30T00:30Z"), _cron.GetNextOccurrence("30 3 * * *", Kyiv, first!.Value));
    }

    [Fact]
    public void FallBack_InKyiv_ARepeatedTimeRunsOnce()
    {
        // 03:30 happens twice on 25 October (00:30Z in EEST, 01:30Z in EET): it runs at the first.
        var first = _cron.GetNextOccurrence("30 3 * * *", Kyiv, At("2026-10-24T12:00Z"));
        Assert.Equal(At("2026-10-25T00:30Z"), first);
        Assert.Equal(At("2026-10-26T01:30Z"), _cron.GetNextOccurrence("30 3 * * *", Kyiv, first!.Value));
    }

    [Fact]
    public void FallBack_InKyiv_AnIntervalKeepsItsPaceThroughTheRepeatedHour()
    {
        var occurrences = Occurrences("*/30 * * * *", Kyiv, At("2026-10-24T23:45Z"), 6);

        Assert.Equal(
            [At("2026-10-25T00:00Z"), At("2026-10-25T00:30Z"), At("2026-10-25T01:00Z"), At("2026-10-25T01:30Z"), At("2026-10-25T02:00Z"), At("2026-10-25T02:30Z")],
            occurrences);
    }

    [Fact]
    public void Inclusive_CountsAnOccurrenceAtTheStartingTime()
    {
        Assert.Equal(At("2026-10-03T10:31Z"), _cron.GetNextOccurrence("*/1 * * * *", null, At("2026-10-03T10:31Z"), inclusive: true));
        Assert.Equal(At("2026-10-03T10:32Z"), _cron.GetNextOccurrence("*/1 * * * *", null, At("2026-10-03T10:31Z")));
    }

    [Fact]
    public void InvalidInput_HasNoNextOccurrence()
    {
        Assert.Null(_cron.GetNextOccurrence("not cron", null, At("2026-10-03T10:00Z")));
        Assert.Null(_cron.GetNextOccurrence("0 9 * * *", "Mars/Olympus", At("2026-10-03T10:00Z")));
    }

    private List<DateTimeOffset> Occurrences(string expression, string? timeZone, DateTimeOffset from, int count)
    {
        var result = new List<DateTimeOffset>();
        while (result.Count < count && _cron.GetNextOccurrence(expression, timeZone, from) is { } next)
        {
            result.Add(next);
            from = next;
        }

        return result;
    }

    private static DateTimeOffset At(string utc) => DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture);
}
