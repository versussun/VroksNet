using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VroksNet.Infrastructure.Specifications;

/// <summary>
/// Builds an example from a JSON Schema, for an operation whose spec has none — so the mock, Send
/// scenarios and Publishers still have something that matches the contract instead of "{}".
/// Deterministic and best-effort:
/// <list type="bullet">
/// <item>A value the schema gives wins: <c>examples[0]</c>, <c>example</c>, <c>const</c>,
/// <c>default</c>, <c>enum[0]</c> — at any level, so a property's own example is used.</item>
/// <item>Objects get every declared property; arrays one item (or <c>minItems</c>);
/// <c>allOf</c> is merged, <c>oneOf</c>/<c>anyOf</c> take their first branch.</item>
/// <item>Strings follow their <c>format</c> — <c>uuid</c> and <c>date-time</c> become the
/// <c>{{uuid}}</c>/<c>{{now}}</c> placeholders, filled per use — and their length limits; numbers
/// their bounds. A <c>pattern</c> can't be honoured, so such a string may not match it.</item>
/// <item><c>$ref</c>s resolve against the schema's own <c>$defs</c>/<c>definitions</c>, or through
/// the caller's resolver; a cycle or depth past <see cref="MaxDepth"/> leaves the property out.</item>
/// </list>
/// </summary>
public static class SchemaExampleGenerator
{
    private const int MaxDepth = 12;
    /// <summary>A sanity cap on <c>minItems</c>: an example with more items than this isn't built in full.</summary>
    private const int MaxArrayItems = 1000;

    private static readonly JsonSerializerOptions ExampleJsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// An example for <paramref name="schemaJson"/>, as indented JSON; null if there's no schema or
    /// nothing could be built from it (e.g. it's just <c>true</c>).
    /// </summary>
    /// <param name="resolveRef">Resolves a <c>$ref</c> the schema itself doesn't hold (e.g. an AsyncAPI "#/components/schemas/X"); null if it can't.</param>
    public static string? Generate(string? schemaJson, Func<string, JsonNode?>? resolveRef = null)
    {
        if (string.IsNullOrWhiteSpace(schemaJson))
        {
            return null;
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(schemaJson);
        }
        catch (JsonException)
        {
            return null;
        }

        var generated = new Generation(root, resolveRef).Of(root, depth: 0, new HashSet<string>());
        return generated.Exists ? (generated.Value?.ToJsonString(ExampleJsonOptions) ?? "null") : null;
    }

    /// <summary>A generated value; <see cref="Exists"/> is false when nothing could be built (which differs from a JSON null).</summary>
    private readonly record struct Generated(bool Exists, JsonNode? Value)
    {
        public static Generated Nothing => new(false, null);

        public static Generated Of(JsonNode? value) => new(true, value);
    }

    private sealed class Generation(JsonNode? root, Func<string, JsonNode?>? resolveRef)
    {
        /// <param name="variant">
        /// Which of an array's items this is (0 elsewhere): items differ by it — the next enum value
        /// or example, a number a step on, a numbered string — so <c>uniqueItems</c> holds.
        /// </param>
        public Generated Of(JsonNode? schema, int depth, IReadOnlySet<string> refsInProgress, int variant = 0)
        {
            if (schema is not JsonObject node || depth > MaxDepth)
            {
                return Generated.Nothing;
            }

            if (GivenValue(node, variant) is { Exists: true } given)
            {
                return given;
            }

            if (node["$ref"] is JsonValue refValue && refValue.TryGetValue<string>(out var reference))
            {
                return refsInProgress.Contains(reference) || Resolve(reference) is not { } target
                    ? Generated.Nothing
                    : Of(target, depth + 1, new HashSet<string>(refsInProgress) { reference }, variant);
            }

            if (node["allOf"] is JsonArray allOf)
            {
                return AllOf(node, allOf, depth, refsInProgress, variant);
            }

            foreach (var keyword in new[] { "oneOf", "anyOf" })
            {
                if (node[keyword] is JsonArray branches)
                {
                    // The first branch that isn't just "null", else the first that yields anything.
                    var ordered = branches.OrderBy(branch => IsNullOnly(branch) ? 1 : 0);
                    foreach (var branch in ordered)
                    {
                        if (Of(branch, depth + 1, refsInProgress, variant) is { Exists: true } chosen)
                        {
                            return chosen;
                        }
                    }

                    return Generated.Nothing;
                }
            }

            return TypeOf(node) switch
            {
                "object" => Generated.Of(ObjectOf(node, depth, refsInProgress, variant)),
                "array" => Generated.Of(ArrayOf(node, depth, refsInProgress)),
                "string" => Generated.Of(JsonValue.Create(StringOf(node, variant))),
                "integer" => Generated.Of(JsonValue.Create((long)NumberOf(node, integer: true, variant))),
                "number" => Generated.Of(JsonValue.Create(NumberOf(node, integer: false, variant))),
                "boolean" => Generated.Of(JsonValue.Create(variant % 2 == 0)),
                "null" => Generated.Of(null),
                _ => Generated.Nothing
            };
        }

        /// <summary>A value the schema states itself, in order of how directly it's meant as one.</summary>
        private static Generated GivenValue(JsonObject node, int variant)
        {
            if (node["examples"] is JsonArray { Count: > 0 } examples)
            {
                return Generated.Of(examples[variant % examples.Count]?.DeepClone());
            }

            foreach (var keyword in new[] { "example", "const", "default" })
            {
                if (node.TryGetPropertyValue(keyword, out var value))
                {
                    return Generated.Of(value?.DeepClone());
                }
            }

            if (node["enum"] is not JsonArray { Count: > 0 } values)
            {
                return Generated.Nothing;
            }

            // A real value before a null one, when the enum has any.
            var choices = values.Where(value => value is not null).ToList() is { Count: > 0 } real ? real : [.. values];
            return Generated.Of(choices[variant % choices.Count]?.DeepClone());
        }

        private JsonNode? Resolve(string reference)
        {
            foreach (var prefix in new[] { "#/$defs/", "#/definitions/" })
            {
                if (reference.StartsWith(prefix, StringComparison.Ordinal))
                {
                    var name = Uri.UnescapeDataString(reference[prefix.Length..]).Replace("~1", "/").Replace("~0", "~");
                    if (root?[prefix[2..^1]]?[name] is { } local)
                    {
                        return local;
                    }
                }
            }

            return reference == "#" ? root : resolveRef?.Invoke(reference);
        }

        private Generated AllOf(JsonObject node, JsonArray allOf, int depth, IReadOnlySet<string> refsInProgress, int variant)
        {
            var parts = allOf.Select(part => Of(part, depth + 1, refsInProgress, variant)).Where(part => part.Exists).ToList();
            if (node.ContainsKey("properties"))
            {
                parts.Add(Generated.Of(ObjectOf(node, depth, refsInProgress, variant)));
            }

            if (parts.Count > 0 && parts.All(part => part.Value is JsonObject))
            {
                var merged = new JsonObject();
                foreach (var part in parts)
                {
                    foreach (var (name, value) in (JsonObject)part.Value!) // all are objects, checked above
                    {
                        merged[name] = value?.DeepClone();
                    }
                }

                return Generated.Of(merged);
            }

            return parts.FirstOrDefault();
        }

        private JsonObject ObjectOf(JsonObject node, int depth, IReadOnlySet<string> refsInProgress, int variant)
        {
            var result = new JsonObject();
            if (node["properties"] is JsonObject properties)
            {
                foreach (var (name, propertySchema) in properties)
                {
                    if (Of(propertySchema, depth + 1, refsInProgress, variant) is { Exists: true } value)
                    {
                        result[name] = value.Value;
                    }
                }
            }

            return result;
        }

        private JsonArray ArrayOf(JsonObject node, int depth, IReadOnlySet<string> refsInProgress)
        {
            var result = new JsonArray();
            if (node["prefixItems"] is JsonArray prefixItems)
            {
                foreach (var itemSchema in prefixItems)
                {
                    if (Of(itemSchema, depth + 1, refsInProgress) is { Exists: true } value)
                    {
                        result.Add(value.Value);
                    }
                }

                return result;
            }

            var count = Math.Min(MaxArrayItems, Math.Max(1, IntegerOf(node, "minItems") ?? 1));
            if (IntegerOf(node, "maxItems") is { } maxItems)
            {
                count = Math.Min(count, maxItems);
            }

            for (var i = 0; i < count; i++)
            {
                if (Of(node["items"], depth + 1, refsInProgress, variant: i) is not { Exists: true } item)
                {
                    break;
                }

                result.Add(item.Value);
            }

            return result;
        }

        private static string StringOf(JsonObject node, int variant)
        {
            var format = (node["format"] as JsonValue)?.TryGetValue<string>(out var value) == true ? value : null;
            switch (format)
            {
                // Filled in each time the example is used (see ResponseTemplateEngine).
                case "uuid":
                    return "{{uuid}}";
                case "date-time":
                    return "{{now}}";
            }

            // The first item has the plain value; later ones are numbered, so an array's items differ.
            var number = variant + 1;
            var suffix = variant == 0 ? string.Empty : number.ToString(CultureInfo.InvariantCulture);
            var text = format switch
            {
                "date" => new DateOnly(2024, 1, 1).AddDays(variant).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                "time" => new TimeOnly(12, 0).Add(TimeSpan.FromSeconds(variant)).ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "Z",
                "email" or "idn-email" => $"user{suffix}@example.com",
                "uri" or "url" or "iri" => variant == 0 ? "https://example.com" : $"https://example.com/{number}",
                "uri-reference" or "iri-reference" => variant == 0 ? "/example" : $"/example/{number}",
                "hostname" or "idn-hostname" => variant == 0 ? "example.com" : $"host{number}.example.com",
                "ipv4" => $"192.0.2.{1 + variant % 254}",
                "ipv6" => $"2001:db8::{(1 + variant).ToString("x", CultureInfo.InvariantCulture)}",
                "duration" => $"PT{number}H",
                "byte" => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"example{suffix}")),
                null => $"string{suffix}",
                _ => "string"
            };

            if (IntegerOf(node, "minLength") is { } minLength && text.Length < minLength)
            {
                text = text.PadRight(minLength, 'x');
            }

            if (IntegerOf(node, "maxLength") is { } maxLength && text.Length > maxLength)
            {
                // Keep the number when cutting, or the items would be equal again.
                text = suffix.Length > 0 && suffix.Length < maxLength ? text[..(maxLength - suffix.Length)] + suffix : text[..maxLength];
            }

            return text;
        }

        /// <summary>
        /// A number within the schema's bounds and on its multipleOf: 0 if that fits, else the value
        /// nearest the lower bound, else the one nearest the upper. An exclusive bound on a number
        /// that isn't on a grid takes the midpoint of the two bounds (or one away from a lone bound).
        /// </summary>
        private static double NumberOf(JsonObject node, bool integer, int variant)
        {
            var multipleOf = DoubleOf(node, "multipleOf") is > 0 and var given ? given : (double?)null;
            // An integer's grid: its multipleOf if that's whole, else 1. A number has none without one.
            double? grid = integer ? (multipleOf is { } whole && whole == Math.Floor(whole) ? whole : 1) : multipleOf;

            var (lower, lowerExclusive) = BoundOf(node, "minimum", "exclusiveMinimum");
            var (upper, upperExclusive) = BoundOf(node, "maximum", "exclusiveMaximum");

            bool Fits(double value)
                => (lower is not { } low || (lowerExclusive ? value > low : value >= low))
                    && (upper is not { } high || (upperExclusive ? value < high : value <= high))
                    && (grid is not { } step || Math.Abs(value / step - Math.Round(value / step)) < 1e-9);

            double? nearLower = lower is { } min
                ? grid is { } lowStep
                    ? Snap(Math.Ceiling(min / lowStep) * lowStep is var up && lowerExclusive && up <= min ? up + lowStep : up)
                    : lowerExclusive ? (upper is { } max ? (min + max) / 2 : min + 1) : min
                : null;
            double? nearUpper = upper is { } top
                ? grid is { } highStep
                    ? Snap(Math.Floor(top / highStep) * highStep is var down && upperExclusive && down >= top ? down - highStep : down)
                    : upperExclusive ? (lower is { } bottom ? (bottom + top) / 2 : top - 1) : top
                : null;

            var chosen = new[] { 0, nearLower, nearUpper }.FirstOrDefault(candidate => candidate is { } value && Fits(value))
                ?? nearLower ?? nearUpper ?? 0;
            if (variant == 0)
            {
                return chosen;
            }

            // Another item of an array: some steps away, on whichever side still fits.
            var offset = variant * (grid ?? 1);
            return new[] { Snap(chosen + offset), Snap(chosen - offset) }.FirstOrDefault(Fits, chosen);
        }

        /// <summary>
        /// A bound and whether it's exclusive: draft 6+ (and OpenAPI 3.1) give an exclusive bound as
        /// its own number, draft 4 (and OpenAPI 3.0) as a flag on the inclusive one.
        /// </summary>
        private static (double? Bound, bool Exclusive) BoundOf(JsonObject node, string inclusive, string exclusive)
        {
            if (DoubleOf(node, exclusive) is { } exclusiveBound)
            {
                return (exclusiveBound, true);
            }

            var bound = DoubleOf(node, inclusive);
            var flagged = node[exclusive] is JsonValue flag && flag.GetValueKind() is JsonValueKind.True;
            return (bound, bound is not null && flagged);
        }

        /// <summary>Drops the floating-point noise a grid step leaves (0.1 * 3 → 0.3).</summary>
        private static double Snap(double value) => Math.Round(value, 10);

        /// <summary>The schema's type: its "type" (the first that isn't "null", if it lists several), else what its keywords imply.</summary>
        private static string? TypeOf(JsonObject node)
        {
            switch (node["type"])
            {
                case JsonValue single when single.TryGetValue<string>(out var type):
                    return type;
                case JsonArray types:
                    var names = types.Select(type => (type as JsonValue)?.TryGetValue<string>(out var name) == true ? name : null).OfType<string>().ToList();
                    return names.FirstOrDefault(name => name != "null") ?? names.FirstOrDefault();
            }

            return node.ContainsKey("properties") || node.ContainsKey("required") || node.ContainsKey("additionalProperties") ? "object"
                : node.ContainsKey("items") || node.ContainsKey("prefixItems") ? "array"
                : node.ContainsKey("minLength") || node.ContainsKey("maxLength") || node.ContainsKey("pattern") || node.ContainsKey("format") ? "string"
                : node.ContainsKey("minimum") || node.ContainsKey("maximum") || node.ContainsKey("multipleOf") ? "number"
                : null;
        }

        private static bool IsNullOnly(JsonNode? schema)
            => schema is JsonObject node && node["type"] is JsonValue type && type.TryGetValue<string>(out var name) && name == "null";

        private static int? IntegerOf(JsonObject node, string keyword)
            => DoubleOf(node, keyword) is { } value && value >= 0 ? (int)Math.Min(value, int.MaxValue) : null;

        /// <summary>
        /// Read through the number's JSON text: a schema from YAML holds a <c>long</c> or <c>decimal</c>
        /// rather than a <see cref="JsonElement"/>, which <c>GetValue&lt;double&gt;</c> won't convert.
        /// </summary>
        private static double? DoubleOf(JsonObject node, string keyword)
            => node[keyword] is JsonValue value && value.GetValueKind() == JsonValueKind.Number
                && double.TryParse(value.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                ? number
                : null;
    }
}
