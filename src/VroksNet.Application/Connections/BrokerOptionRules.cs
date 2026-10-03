using VroksNet.Application.Abstractions;
using VroksNet.Domain.Connections;

namespace VroksNet.Application.Connections;

/// <summary>
/// Turns what a create/update request says about broker options into what's stored — shared by
/// Test Scenarios and Publishers, and by the API and provisioning, so they all agree.
/// </summary>
internal static class BrokerOptionRules
{
    /// <summary>The option that the deprecated <c>exchange</c> field of contract v1 stands for.</summary>
    public const string ExchangeOption = "exchange";

    /// <summary>
    /// Merges the deprecated <paramref name="exchange"/> into <paramref name="brokerOptions"/> and
    /// checks the result against what <paramref name="type"/> accepts: names are matched
    /// case-insensitively and stored as the adapter spells them, blank values mean "the default".
    /// <paramref name="exchange"/> is dropped, as it always was, for a type that has no such option.
    /// Throws <see cref="ArgumentException"/> on an option the type doesn't know, a value outside its
    /// <see cref="BrokerOptionDefinition.AllowedValues"/> (stored as listed), or when
    /// <paramref name="exchange"/> and <c>brokerOptions.exchange</c> disagree.
    /// </summary>
    public static BrokerOptions? Normalize(IBrokerRules rules, ConnectionServiceType type, string? exchange, IReadOnlyDictionary<string, string?>? brokerOptions)
    {
        var requested = BrokerOptions.From(brokerOptions);
        var definitions = rules.OptionsOf(type);
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var (name, value) in requested?.Values ?? new Dictionary<string, string>())
        {
            var definition = definitions.FirstOrDefault(option => string.Equals(option.Name, name, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException(definitions.Count == 0
                    ? $"A {type} connection takes no broker options, but \"{name}\" was given."
                    : $"Unknown broker option \"{name}\" for a {type} connection — it accepts {string.Join(", ", definitions.Select(option => $"\"{option.Name}\""))}.");
            values[definition.Name] = AllowedValueOf(definition, value, type);
        }

        if (!string.IsNullOrWhiteSpace(exchange)
            && definitions.FirstOrDefault(option => option.Name == ExchangeOption) is not null)
        {
            var legacy = exchange.Trim();
            if (values.TryGetValue(ExchangeOption, out var given) && given != legacy)
            {
                throw new ArgumentException($"\"exchange\" (\"{legacy}\") and \"brokerOptions.exchange\" (\"{given}\") disagree — set one of them.");
            }

            values[ExchangeOption] = legacy;
        }

        return BrokerOptions.From(values);
    }

    private static string AllowedValueOf(BrokerOptionDefinition definition, string value, ConnectionServiceType type)
    {
        if (definition.AllowedValues is not { } allowed)
        {
            return value;
        }

        return allowed.FirstOrDefault(candidate => string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Broker option \"{definition.Name}\" of a {type} connection can't be \"{value}\" — it takes {string.Join(", ", allowed.Select(candidate => $"\"{candidate}\""))}.");
    }
}
