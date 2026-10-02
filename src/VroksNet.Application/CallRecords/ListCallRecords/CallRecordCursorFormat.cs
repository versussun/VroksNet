using System.Globalization;
using VroksNet.Application.Abstractions;

namespace VroksNet.Application.CallRecords.ListCallRecords;

/// <summary>The opaque string form of a <see cref="CallRecordCursor"/> handed to API clients: "{UTC ticks}-{id}".</summary>
internal static class CallRecordCursorFormat
{
    public static string Format(CallRecordCursor cursor)
        => $"{cursor.Timestamp.UtcTicks.ToString(CultureInfo.InvariantCulture)}-{cursor.Id:N}";

    /// <exception cref="ArgumentException">The token isn't one <see cref="Format"/> produced.</exception>
    public static CallRecordCursor Parse(string token)
    {
        var parts = token.Split('-', 2);
        if (parts.Length == 2
            && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            && ticks <= DateTimeOffset.MaxValue.UtcTicks
            && Guid.TryParseExact(parts[1], "N", out var id))
        {
            return new CallRecordCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id);
        }

        // Deliberately doesn't echo the client-supplied token, which would end up in error logs.
        throw new ArgumentException("Invalid call-history cursor.", nameof(token));
    }
}
