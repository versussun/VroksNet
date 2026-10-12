using System.Globalization;
using System.Text.RegularExpressions;

namespace VroksNet.Infrastructure.Protobuf;

/// <summary>The string forms the proto3 JSON mapping gives Timestamp, Duration and FieldMask.</summary>
internal static partial class WellKnownJson
{
    public const string Timestamp = "google.protobuf.Timestamp";
    public const string Duration = "google.protobuf.Duration";
    public const string FieldMask = "google.protobuf.FieldMask";
    public const string Struct = "google.protobuf.Struct";
    public const string Value = "google.protobuf.Value";
    public const string ListValue = "google.protobuf.ListValue";
    public const string Empty = "google.protobuf.Empty";
    public const string Any = "google.protobuf.Any";
    public const string NullValue = "google.protobuf.NullValue";

    private const long MinTimestampSeconds = -62135596800; // 0001-01-01T00:00:00Z
    private const long MaxTimestampSeconds = 253402300799; // 9999-12-31T23:59:59Z
    private const long MaxDurationSeconds = 315576000000;  // 10 000 years
    private const int NanosPerSecond = 1_000_000_000;

    private static readonly HashSet<string> Wrappers = new(StringComparer.Ordinal)
    {
        "google.protobuf.DoubleValue", "google.protobuf.FloatValue", "google.protobuf.Int64Value", "google.protobuf.UInt64Value",
        "google.protobuf.Int32Value", "google.protobuf.UInt32Value", "google.protobuf.BoolValue", "google.protobuf.StringValue",
        "google.protobuf.BytesValue"
    };

    /// <summary>A type whose JSON isn't an object of its fields.</summary>
    public static bool IsSpecial(string fullName)
        => IsWrapper(fullName) || fullName is Timestamp or Duration or FieldMask or Struct or Value or ListValue or Any;

    public static bool IsWrapper(string fullName) => Wrappers.Contains(fullName);

    public static (long Seconds, int Nanos) ParseTimestamp(string text, string path)
    {
        var match = TimestampPattern().Match(text);
        if (!match.Success)
        {
            throw ProtobufTranscodeException.At(path, $"\"{text}\" isn't an RFC 3339 timestamp like \"2026-10-11T09:30:00Z\".");
        }

        DateTime local;
        try
        {
            local = new DateTime(Int(match, "year"), Int(match, "month"), Int(match, "day"), Int(match, "hour"), Int(match, "minute"), Int(match, "second"), DateTimeKind.Utc);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw ProtobufTranscodeException.At(path, $"\"{text}\" isn't a valid date and time.");
        }

        var offset = match.Groups["offset"].Value is "Z" or "z"
            ? TimeSpan.Zero
            : new TimeSpan(int.Parse(match.Groups["offset"].Value[1..3], CultureInfo.InvariantCulture), int.Parse(match.Groups["offset"].Value[4..6], CultureInfo.InvariantCulture), 0)
                * (match.Groups["offset"].Value[0] == '-' ? -1 : 1);
        var seconds = (long)(local - offset - DateTime.UnixEpoch).TotalSeconds;
        if (seconds is < MinTimestampSeconds or > MaxTimestampSeconds)
        {
            throw ProtobufTranscodeException.At(path, $"\"{text}\" is outside the years 0001–9999 a Timestamp can hold.");
        }

        return (seconds, Nanos(match.Groups["fraction"].Value));
    }

    public static string FormatTimestamp(long seconds, int nanos, string path)
    {
        if (seconds is < MinTimestampSeconds or > MaxTimestampSeconds || nanos is < 0 or >= NanosPerSecond)
        {
            throw ProtobufTranscodeException.At(path, $"a Timestamp of {seconds} s and {nanos} ns is out of range.");
        }

        var time = DateTime.UnixEpoch.AddSeconds(seconds);
        return time.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss", CultureInfo.InvariantCulture) + Fraction(nanos) + "Z";
    }

    public static (long Seconds, int Nanos) ParseDuration(string text, string path)
    {
        var match = DurationPattern().Match(text);
        if (!match.Success || !long.TryParse(match.Groups["seconds"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) || seconds > MaxDurationSeconds)
        {
            throw ProtobufTranscodeException.At(path, $"\"{text}\" isn't a duration like \"1.5s\" (at most {MaxDurationSeconds} seconds).");
        }

        var nanos = Nanos(match.Groups["fraction"].Value);
        return match.Groups["sign"].Success ? (-seconds, -nanos) : (seconds, nanos);
    }

    public static string FormatDuration(long seconds, int nanos, string path)
    {
        if (Math.Abs(seconds) > MaxDurationSeconds || Math.Abs(nanos) >= NanosPerSecond || (seconds != 0 && nanos != 0 && (seconds < 0) != (nanos < 0)))
        {
            throw ProtobufTranscodeException.At(path, $"a Duration of {seconds} s and {nanos} ns is out of range.");
        }

        var negative = seconds < 0 || nanos < 0;
        return $"{(negative ? "-" : string.Empty)}{Math.Abs(seconds).ToString(CultureInfo.InvariantCulture)}{Fraction(Math.Abs(nanos))}s";
    }

    /// <summary>"fooBar,baz.quxQuux" → ["foo_bar", "baz.qux_quux"].</summary>
    public static IEnumerable<string> ParseFieldMask(string text)
        => text.Length == 0 ? [] : text.Split(',').Select(path => CamelCaseToSnake().Replace(path, match => "_" + char.ToLowerInvariant(match.Value[0])));

    /// <summary>["foo_bar"] → "fooBar".</summary>
    public static string FormatFieldMask(IEnumerable<string> paths)
        => string.Join(',', paths.Select(path => SnakeCaseToCamel().Replace(path, match => char.ToUpperInvariant(match.Value[1]).ToString())));

    /// <summary>Nothing for whole seconds, else 3, 6 or 9 digits — as few as hold the value.</summary>
    private static string Fraction(int nanos)
    {
        if (nanos == 0)
        {
            return string.Empty;
        }

        var digits = nanos.ToString("D9", CultureInfo.InvariantCulture);
        return "." + (nanos % 1_000_000 == 0 ? digits[..3] : nanos % 1000 == 0 ? digits[..6] : digits);
    }

    private static int Nanos(string fraction)
        => fraction.Length == 0 ? 0 : int.Parse(fraction[1..].PadRight(9, '0'), CultureInfo.InvariantCulture);

    private static int Int(Match match, string group) => int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^(?<year>\d{4})-(?<month>\d{2})-(?<day>\d{2})[Tt](?<hour>\d{2}):(?<minute>\d{2}):(?<second>\d{2})(?<fraction>\.\d{1,9})?(?<offset>[Zz]|[+-]\d{2}:\d{2})$")]
    private static partial Regex TimestampPattern();

    [GeneratedRegex(@"^(?<sign>-)?(?<seconds>\d+)(?<fraction>\.\d{1,9})?s$")]
    private static partial Regex DurationPattern();

    [GeneratedRegex("[A-Z]")]
    private static partial Regex CamelCaseToSnake();

    [GeneratedRegex("_[a-z0-9]")]
    private static partial Regex SnakeCaseToCamel();
}
