namespace VroksNet.Domain.Connections;

/// <summary>
/// Broker-specific settings of a Test Scenario or Publisher (ADR 0003), e.g. RabbitMQ's
/// <c>exchange</c>. Which names a connection type accepts is up to its broker adapter, which
/// declares them; Application checks a value against that list before storing it. Immutable, and
/// never empty: an object with no options is null, not an empty <see cref="BrokerOptions"/>.
/// </summary>
public sealed class BrokerOptions : IEquatable<BrokerOptions>
{
    private readonly SortedDictionary<string, string> values;

    private BrokerOptions(SortedDictionary<string, string> values) => this.values = values;

    /// <summary>The options by name, in name order.</summary>
    public IReadOnlyDictionary<string, string> Values => values;

    /// <summary>The value of option <paramref name="name"/>; null if it isn't set.</summary>
    public string? this[string name] => values.GetValueOrDefault(name);

    /// <summary>
    /// Options from name/value pairs, with names and values trimmed and blank values dropped
    /// (blank means "the default"). Null if nothing is left. A later pair wins over an earlier one
    /// with the same name.
    /// </summary>
    public static BrokerOptions? From(IEnumerable<KeyValuePair<string, string?>>? pairs)
    {
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in pairs ?? [])
        {
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(value))
            {
                values[name.Trim()] = value.Trim();
            }
        }

        return values.Count == 0 ? null : new BrokerOptions(values);
    }

    /// <summary>These options without <paramref name="name"/>; null if that leaves none.</summary>
    public BrokerOptions? Without(string name)
        => From(values.Where(pair => pair.Key != name).Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)));

    public bool Equals(BrokerOptions? other)
        => other is not null && values.Count == other.values.Count && values.All(pair => other.values.TryGetValue(pair.Key, out var value) && value == pair.Value);

    public override bool Equals(object? obj) => Equals(obj as BrokerOptions);

    public override int GetHashCode() => values.Aggregate(0, (hash, pair) => hash ^ HashCode.Combine(pair.Key, pair.Value));
}
