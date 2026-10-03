using System.Globalization;
using System.Text.Json.Nodes;
using SharpYaml;
using SharpYaml.Serialization;

namespace VroksNet.Infrastructure.Yaml;

/// <summary>
/// YAML → <see cref="JsonNode"/> over SharpYaml's representation model, shared by the AsyncAPI
/// parser and the provisioning manifest reader. A plain (unquoted) scalar carries no type tag, so
/// it's typed the way YAML's core schema would (bool, integer, float, else string); a quoted
/// scalar always stays a string, matching how the source YAML wrote it.
/// </summary>
internal static class YamlJson
{
    /// <summary>The document's root mapping. Throws <see cref="InvalidOperationException"/> when the text isn't YAML, or its root isn't a mapping.</summary>
    public static YamlMappingNode LoadMapping(string yaml, string what)
    {
        var stream = new YamlStream();
        try
        {
            using var reader = new StringReader(yaml);
            stream.Load(reader);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException($"Failed to parse {what}: {ex.Message}", ex);
        }

        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            throw new InvalidOperationException($"Failed to parse {what}: empty document, or its root isn't a mapping.");
        }

        return root;
    }

    public static JsonNode? ToJson(YamlNode? node) => node switch
    {
        null => null,
        YamlScalarNode scalar => ScalarToJson(scalar),
        YamlSequenceNode sequence => new JsonArray(sequence.Children.Select(ToJson).ToArray()),
        YamlMappingNode mapping => new JsonObject(mapping.Children
            .Where(pair => pair.Key is YamlScalarNode)
            .Select(pair => KeyValuePair.Create(((YamlScalarNode)pair.Key).Value ?? string.Empty, ToJson(pair.Value)))),
        _ => null
    };

    private static JsonNode? ScalarToJson(YamlScalarNode scalar)
    {
        var value = scalar.Value;
        if (value is null)
        {
            return null;
        }

        if (scalar.Style == ScalarStyle.Plain)
        {
            if (value is "null" or "~" or "")
            {
                return null;
            }

            if (bool.TryParse(value, out var boolValue))
            {
                return JsonValue.Create(boolValue);
            }

            if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue))
            {
                return JsonValue.Create(longValue);
            }

            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
            {
                return JsonValue.Create(doubleValue);
            }
        }

        return JsonValue.Create(value);
    }
}
