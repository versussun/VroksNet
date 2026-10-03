using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using SharpYaml;
using SharpYaml.Serialization;
using VroksNet.Application.Abstractions;
using VroksNet.Infrastructure.Yaml;

namespace VroksNet.Infrastructure.Specifications;

/// <summary>
/// Parses an AsyncAPI 3.0 YAML document into a minimal <see cref="ParsedSpecification"/> — one
/// operation per top-level <c>operations</c> entry, keyed as "{channel address}:{action}" (the
/// convention documented on <c>MockEndpoint.OperationKey</c>), each carrying its first message's
/// first example payload (if any) pretty-printed as JSON, plus that message's payload schema
/// (self-contained — local $refs resolved) for the contract-testing checks described in
/// docs/contract-testing-plan.md.
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

        return Task.FromResult(new ParsedSpecification(title, operations, ProtocolsOf(root)));
    }

    /// <summary>
    /// The distinct <c>servers.*.protocol</c> values, trimmed and lower-cased, in the order they
    /// appear — the same shape in AsyncAPI 2 and 3. A server may be a local <c>$ref</c>
    /// (<c>#/components/servers/prod</c>); it's followed one hop, like channels and messages.
    /// </summary>
    private static List<string> ProtocolsOf(YamlMappingNode root)
        => root.Child("servers") is YamlMappingNode servers
            ? servers.Children
                .Select(server => (server.Value.Child("$ref").AsString() is { } pointer ? Resolve(root, pointer) : server.Value)
                    .Child("protocol").AsString()?.Trim().ToLowerInvariant())
                .OfType<string>()
                .Where(protocol => protocol.Length > 0)
                .Distinct()
                .ToList()
            : [];

    private static YamlMappingNode LoadRoot(string rawContent) => YamlJson.LoadMapping(rawContent, "AsyncAPI document");

    private static ParsedOperation? ParseOperation(YamlMappingNode root, YamlMappingNode operation)
    {
        var action = operation.Child("action").AsString();
        var channel = Resolve(root, operation.Child("channel").Child("$ref").AsString());
        var address = channel.Child("address").AsString();

        if (action is null || address is null)
        {
            return null;
        }

        var message = ResolveFirstMessage(root, operation);

        // ResponseSchemaJson doubles as "the message payload schema" for AsyncAPI — see
        // ParsedOperation's doc comment.
        return new ParsedOperation(
            $"{address}:{action}",
            ExtractExampleJson(message),
            ResponseSchemaJson: ExtractPayloadSchemaJson(root, message));
    }

    /// <summary>
    /// Resolves the operation's first referenced message, following the usual two-hop chain:
    /// operation.messages[0].$ref → the channel's message entry → (if that's itself just a $ref,
    /// which it usually is) components.messages.*.
    /// </summary>
    private static YamlMappingNode? ResolveFirstMessage(YamlMappingNode root, YamlMappingNode operation)
    {
        var firstMessageRef = (operation.Child("messages") as YamlSequenceNode)?.Children.FirstOrDefault();
        var channelMessage = Resolve(root, firstMessageRef.Child("$ref").AsString());

        return (channelMessage.Child("$ref").AsString() is { } componentRef
            ? Resolve(root, componentRef)
            : channelMessage) as YamlMappingNode;
    }

    /// <summary>The first example payload of the message, if any.</summary>
    private static string? ExtractExampleJson(YamlMappingNode? message)
    {
        var examplePayload = (message.Child("examples") as YamlSequenceNode)?.Children
            .Select(example => example.Child("payload"))
            .FirstOrDefault(payload => payload is not null);

        return YamlJson.ToJson(examplePayload)?.ToJsonString(ExampleJsonOptions);
    }

    /// <summary>The message's payload schema — resolving one more $ref hop if "payload" is itself just a reference into components.schemas.*, so the extracted schema is self-contained.</summary>
    private static string? ExtractPayloadSchemaJson(YamlMappingNode root, YamlMappingNode? message)
    {
        var payload = message.Child("payload");
        if (payload.Child("$ref").AsString() is { } schemaRef)
        {
            payload = Resolve(root, schemaRef);
        }

        return YamlJson.ToJson(payload)?.ToJsonString(ExampleJsonOptions);
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
}

file static class YamlNodeExtensions
{
    public static YamlNode? Child(this YamlNode? node, string key) =>
        node is YamlMappingNode mapping && mapping.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;

    public static string? AsString(this YamlNode? node) => (node as YamlScalarNode)?.Value;
}
