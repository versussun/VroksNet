namespace VroksNet.Domain.TestScenarios;

/// <summary>
/// The structure of an AsyncAPI channel address, for subscribing to it: its segments, which of them
/// are parameters, and the address's own separator — "." ("orders.{region}.created") or "/"
/// ("user/{id}/signedup"). It knows no broker syntax: each broker adapter renders it in its own
/// wildcards (ADR 0003), and may refuse a pattern its broker can't match.
/// </summary>
public sealed class ChannelPattern
{
    private ChannelPattern(string address, char separator, IReadOnlyList<ChannelSegment> segments)
    {
        Address = address;
        Separator = separator;
        Segments = segments;
    }

    /// <summary>The channel address this was parsed from.</summary>
    public string Address { get; }

    public char Separator { get; }

    public IReadOnlyList<ChannelSegment> Segments { get; }

    public bool HasParameters => Segments.Any(segment => segment.IsParameter);

    /// <summary>
    /// The pattern of <paramref name="channelAddress"/>, split on "." if every parameter is a whole
    /// "."-separated segment there, otherwise on "/". Null if neither works — a parameter that is
    /// only part of a segment ("orders.eu-{region}.created") — since no broker can match that.
    /// </summary>
    public static ChannelPattern? Parse(string channelAddress)
        => Parse(channelAddress, '.') ?? Parse(channelAddress, '/');

    private static ChannelPattern? Parse(string channelAddress, char separator)
    {
        var segments = new List<ChannelSegment>();
        foreach (var segment in channelAddress.Split(separator))
        {
            if (segment.Length > 2 && segment[0] == '{' && segment[^1] == '}' && segment.IndexOfAny(['{', '}'], 1, segment.Length - 2) < 0)
            {
                segments.Add(new ChannelSegment(segment, IsParameter: true));
            }
            else if (segment.Contains('{') || segment.Contains('}'))
            {
                return null;
            }
            else
            {
                segments.Add(new ChannelSegment(segment, IsParameter: false));
            }
        }

        return new ChannelPattern(channelAddress, separator, segments);
    }

    /// <summary>The address with every parameter replaced by <paramref name="wildcard"/> and the literal segments as they are.</summary>
    public string Render(string wildcard)
        => string.Join(Separator, Segments.Select(segment => segment.IsParameter ? wildcard : segment.Text));
}
