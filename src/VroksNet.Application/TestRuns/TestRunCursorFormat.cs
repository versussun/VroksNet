using System.Globalization;
using VroksNet.Application.Abstractions;

namespace VroksNet.Application.TestRuns;

/// <summary>The opaque string form of a <see cref="TestRunCursor"/> handed to API clients: "{UTC ticks}-{id}".</summary>
internal static class TestRunCursorFormat
{
    public static string Format(TestRunCursor cursor)
        => $"{cursor.ScheduledFor.UtcTicks.ToString(CultureInfo.InvariantCulture)}-{cursor.Id:N}";

    /// <exception cref="ArgumentException">The token isn't one <see cref="Format"/> produced.</exception>
    public static TestRunCursor Parse(string token)
    {
        var parts = token.Split('-', 2);
        if (parts.Length == 2
            && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            && ticks <= DateTimeOffset.MaxValue.UtcTicks
            && Guid.TryParseExact(parts[1], "N", out var id))
        {
            return new TestRunCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id);
        }

        // Deliberately doesn't echo the client-supplied token, which would end up in error logs.
        throw new ArgumentException("Invalid test-run cursor.", nameof(token));
    }
}
