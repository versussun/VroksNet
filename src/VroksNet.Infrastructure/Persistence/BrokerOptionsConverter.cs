using System.Text.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VroksNet.Domain.Connections;

namespace VroksNet.Infrastructure.Persistence;

/// <summary>
/// Stores <see cref="BrokerOptions"/> as one JSON object column (<c>{"exchange":"orders"}</c>) —
/// it's only ever read and written whole, with its scenario or Publisher. Null stays SQL NULL: EF
/// doesn't hand nulls to a converter. <see cref="BrokerOptions"/> is immutable with value equality,
/// so EF's default comparer tracks it correctly.
/// </summary>
internal sealed class BrokerOptionsConverter() : ValueConverter<BrokerOptions, string>(
    options => JsonSerializer.Serialize(options.Values, (JsonSerializerOptions?)null),
    json => FromJson(json))
{
    private static BrokerOptions FromJson(string json)
        => BrokerOptions.From(JsonSerializer.Deserialize<Dictionary<string, string?>>(json, (JsonSerializerOptions?)null))
            // An empty object never gets written (no options is NULL), so this only guards a hand-edited row.
            ?? throw new InvalidOperationException("A stored BrokerOptions value has no options; it should be NULL.");
}
