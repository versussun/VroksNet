namespace VroksNet.Application.Abstractions;

/// <summary>One problem in a specification file, located as precisely as the parser can tell.</summary>
public sealed record SpecificationParseError(string? File, int? Line, int? Column, string Message)
{
    /// <summary>"orders.proto(4,13): type not found: 'strin'", or less when the location isn't known.</summary>
    public override string ToString()
    {
        var location = Line is { } line ? $"({line}{(Column is { } column ? $",{column}" : string.Empty)})" : string.Empty;
        return File is null ? Message : $"{File}{location}: {Message}";
    }
}
