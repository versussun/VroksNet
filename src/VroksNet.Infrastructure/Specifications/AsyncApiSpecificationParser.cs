using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using SharpYaml;
using SharpYaml.Serialization;
using VroksNet.Application.Abstractions;

namespace VroksNet.Infrastructure.Specifications;

/// <summary>
/// Parses an AsyncAPI 3.0 YAML document into a minimal <see cref="ParsedSpecification"/> — one
/// operation per top-level <c>operations</c> entry, keyed as "{channel address}:{action}" (the
/// convention documented on <c>MockEndpoint.OperationKey</c>), each carrying its first message's
/// first example payload (if any) pretty-printed as JSON.
///
/// There's no AsyncAPI counterpart to Microsoft.OpenApi's typed object model on NuGet, so this
/// walks the raw YAML tree instead and resolves the spec's own local "#/a/b/c" $refs by hand —
/// deliberately minimal (this spec's own refs only, not a general JSON Reference/external-file
/// implementation). Uses <c>SharpYaml</c>'s representation model (<see cref="YamlMappingNode"/>
/// etc.) rather than adding a second general-purpose YAML library: SharpYaml is already pulled in
/// transitively by <c>Microsoft.OpenApi.YamlReader</c> (see .claude/CLAUDE.md "Infrastructure
/// notes" — same reasoning as for other dependency choices there), so this just references it
/// directly instead of relying on that transitive edge staying stable.
/// </summary>
public sealed class AsyncApiSpecificationParser : IAsyncApiSpecificationParser
{
    private static readonly JsonSerializerOptions ExampleJsonOptions = new() { WriteIndented = true };

    public Task<ParsedSpecification> ParseAsync(string rawContent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var root = LoadRoot(rawContent);

        if (root.Child("asyncapi") is null)
        {
            throw new InvalidOperationException("Failed to parse AsyncAPI document: missing top-level 'asyncapi' version field.");
        }

        var title = root.Child("info").Child("title").AsString() ?? "(untitled)";

        var operations = new List<ParsedOperation>();
        if (root.Child("operations") is YamlMappingNode operationsNode)
        {
            foreach (var entry in operationsNode.Children)
            {
                if (entry.Value is YamlMappingNode operation && ParseOperation(root, operation) is { } parsedOperation)
                {
                    operations.Add(parsedOperation);
                }
            }
        }

        return Task.FromResult(new ParsedSpecification(title, operations));
    }

    private static YamlMappingNode LoadRoot(string rawContent)
    {
        var stream = new YamlStream();
        try
        {
            using var reader = new StringReader(rawContent);
            stream.Load(reader);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException($"Failed to parse AsyncAPI document: {ex.Message}", ex);
        }

        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            throw new InvalidOperationException("Failed to parse AsyncAPI document: empty document, or its root isn't a mapping.");
        }

        return root;
    }

    private static ParsedOperation? ParseOperation(YamlMappingNode root, YamlMappingNode operation)
    {
        var action = operation.Child("action").AsString();
        var channel = Resolve(root, operation.Child("channel").Child("$ref").AsString());
        var address = channel.Child("address").AsString();

        if (action is null || address is null)
        {
            return null;
        }

        return new ParsedOperation($"{address}:{action}", ExtractExampleJson(root, operation));
    }

    /// <summary>The first example payload of the operation's first referenced message, if any.</summary>
    private static string? ExtractExampleJson(YamlMappingNode root, YamlMappingNode operation)
    {
        var firstMessageRef = (operation.Child("messages") as YamlSequenceNode)?.Children.FirstOrDefault();
        var channelMessage = Resolve(root, firstMessageRef.Child("$ref").AsString());

        // A channel's message entry is usually itself just a $ref to components.messages.*.
        var message = channelMessage.Child("$ref").AsString() is { } componentRef
            ? Resolve(root, componentRef)
            : channelMessage;

        var examplePayload = (message.Child("examples") as YamlSequenceNode)?.Children
            .Select(example => example.Child("payload"))
            .FirstOrDefault(payload => payload is not null);

        return ToJson(examplePayload)?.ToJsonString(ExampleJsonOptions);
    }

    /// <summary>Resolves a local JSON-Reference pointer like "#/channels/orderCreated" against the document root.</summary>
    private static YamlNode? Resolve(YamlNode? root, string? pointer)
    {
        if (root is null || pointer is null || !pointer.StartsWith("#/", StringComparison.Ordinal))
        {
            return null;
        }

        var current = root;
        foreach (var segment in pointer[2..].Split('/'))
        {
            current = current.Child(segment);
        }

        return current;
    }

    private static JsonNode? ToJson(YamlNode? node) => node switch
    {
        null => null,
        YamlScalarNode scalar => ScalarToJson(scalar),
        YamlSequenceNode sequence => new JsonArray(sequence.Children.Select(ToJson).ToArray()),
        YamlMappingNode mapping => new JsonObject(mapping.Children
            .Where(pair => pair.Key is YamlScalarNode)
            .Select(pair => KeyValuePair.Create(((YamlScalarNode)pair.Key).Value ?? string.Empty, ToJson(pair.Value)))),
        _ => null
    };

    /// <summary>
    /// A plain (unquoted) scalar carries no type tag in SharpYaml's representation model, so infer
    /// it the way YAML's core schema would; a quoted scalar (<see cref="ScalarStyle.Plain"/> would
    /// be false) always stays a string, matching how the source YAML actually wrote it.
    /// </summary>
    private static JsonNode? ScalarToJson(YamlScalarNode scalar)
    {
        var value = scalar.Value;
        if (value is null)
        {
            return null;
        }

        if (scalar.Style == ScalarStyle.Plain)
        {
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

file static class YamlNodeExtensions
{
    public static YamlNode? Child(this YamlNode? node, string key) =>
        node is YamlMappingNode mapping && mapping.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;

    public static string? AsString(this YamlNode? node) => (node as YamlScalarNode)?.Value;
}
