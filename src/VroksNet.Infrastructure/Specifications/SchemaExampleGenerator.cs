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
    private const int MaxArrayItems = 3;

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
        public Generated Of(JsonNode? schema, int depth, IReadOnlySet<string> refsInProgress)
        {
            if (schema is not JsonObject node || depth > MaxDepth)
            {
                return Generated.Nothing;
            }

            if (GivenValue(node) is { Exists: true } given)
            {
                return given;
            }

            if (node["$ref"] is JsonValue refValue && refValue.TryGetValue<string>(out var reference))
            {
                return refsInProgress.Contains(reference) || Resolve(reference) is not { } target
                    ? Generated.Nothing
                    : Of(target, depth + 1, new HashSet<string>(refsInProgress) { reference });
            }

            if (node["allOf"] is JsonArray allOf)
            {
                return AllOf(node, allOf, depth, refsInProgress);
            }

            foreach (var keyword in new[] { "oneOf", "anyOf" })
            {
                if (node[keyword] is JsonArray branches)
                {
                    // The first branch that isn't just "null", else the first that yields anything.
                    var ordered = branches.OrderBy(branch => IsNullOnly(branch) ? 1 : 0);
                    foreach (var branch in ordered)
                    {
                        if (Of(branch, depth + 1, refsInProgress) is { Exists: true } chosen)
                        {
                            return chosen;
                        }
                    }

                    return Generated.Nothing;
                }
            }

            return TypeOf(node) switch
            {
                "object" => Generated.Of(ObjectOf(node, depth, refsInProgress)),
                "array" => Generated.Of(ArrayOf(node, depth, refsInProgress)),
                "string" => Generated.Of(JsonValue.Create(StringOf(node))),
                "integer" => Generated.Of(JsonValue.Create((long)NumberOf(node, integer: true))),
                "number" => Generated.Of(JsonValue.Create(NumberOf(node, integer: false))),
                "boolean" => Generated.Of(JsonValue.Create(true)),
                "null" => Generated.Of(null),
                _ => Generated.Nothing
            };
        }

        /// <summary>A value the schema states itself, in order of how directly it's meant as one.</summary>
        private static Generated GivenValue(JsonObject node)
        {
            if (node["examples"] is JsonArray { Count: > 0 } examples)
            {
                return Generated.Of(examples[0]?.DeepClone());
            }

            foreach (var keyword in new[] { "example", "const", "default" })
            {
                if (node.TryGetPropertyValue(keyword, out var value))
                {
                    return Generated.Of(value?.DeepClone());
                }
            }

            return node["enum"] is JsonArray { Count: > 0 } values
                ? Generated.Of((values.FirstOrDefault(value => value is not null) ?? values[0])?.DeepClone())
                : Generated.Nothing;
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

        private Generated AllOf(JsonObject node, JsonArray allOf, int depth, IReadOnlySet<string> refsInProgress)
        {
            var parts = allOf.Select(part => Of(part, depth + 1, refsInProgress)).Where(part => part.Exists).ToList();
            if (node.ContainsKey("properties"))
            {
                parts.Add(Generated.Of(ObjectOf(node, depth, refsInProgress)));
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

        private JsonObject ObjectOf(JsonObject node, int depth, IReadOnlySet<string> refsInProgress)
        {
            var result = new JsonObject();
            if (node["properties"] is JsonObject properties)
            {
                foreach (var (name, propertySchema) in properties)
                {
                    if (Of(propertySchema, depth + 1, refsInProgress) is { Exists: true } value)
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

            if (count > 0 && Of(node["items"], depth + 1, refsInProgress) is { Exists: true } item)
            {
                for (var i = 0; i < count; i++)
                {
                    result.Add(item.Value?.DeepClone());
                }
            }

            return result;
        }

        private static string StringOf(JsonObject node)
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

            var text = format switch
            {
                "date" => "2024-01-01",
                "time" => "12:00:00Z",
                "email" or "idn-email" => "user@example.com",
                "uri" or "url" or "iri" => "https://example.com",
                "uri-reference" or "iri-reference" => "/example",
                "hostname" or "idn-hostname" => "example.com",
                "ipv4" => "192.0.2.1",
                "ipv6" => "2001:db8::1",
                "duration" => "PT1H",
                "byte" => "ZXhhbXBsZQ==",
                _ => "string"
            };

            if (IntegerOf(node, "minLength") is { } minLength && text.Length < minLength)
            {
                text = text.PadRight(minLength, 'x');
            }

            if (IntegerOf(node, "maxLength") is { } maxLength && text.Length > maxLength)
            {
                text = text[..maxLength];
            }

            return text;
        }

        /// <summary>0, moved into the schema's bounds (exclusive ones by a step) and onto its multipleOf.</summary>
        private static double NumberOf(JsonObject node, bool integer)
        {
            var step = DoubleOf(node, "multipleOf") is > 0 and var multipleOf ? multipleOf : 1;
            var lower = DoubleOf(node, "minimum");
            var upper = DoubleOf(node, "maximum");
            // JSON Schema draft 6+ gives exclusive bounds as numbers; draft 4 (and OpenAPI 3.0) as flags.
            if (DoubleOf(node, "exclusiveMinimum") is { } exclusiveLower)
            {
                lower = exclusiveLower + step;
            }
            else if (lower is not null && node["exclusiveMinimum"] is JsonValue flag && flag.TryGetValue<bool>(out var exclusive) && exclusive)
            {
                lower += step;
            }

            if (DoubleOf(node, "exclusiveMaximum") is { } exclusiveUpper)
            {
                upper = exclusiveUpper - step;
            }
            else if (upper is not null && node["exclusiveMaximum"] is JsonValue flag && flag.TryGetValue<bool>(out var exclusive) && exclusive)
            {
                upper -= step;
            }

            var number = lower is > 0 ? lower.Value : upper is < 0 ? upper.Value : 0;
            if (step != 1 || integer)
            {
                number = Math.Ceiling(number / step) * step;
            }

            return integer ? Math.Ceiling(number) : number;
        }

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
