using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Path;
using VroksNet.Application.Abstractions;

namespace VroksNet.Infrastructure.Templating;

/// <summary>
/// Fills in a mock response's placeholders (see <see cref="IResponseTemplateEngine"/>) following
/// docs/contract-testing-plan.md 3.9–3.10. A template that starts like JSON (<c>{</c>, <c>[</c> or
/// <c>"</c>) is scanned for string literals, so the result stays valid JSON:
/// <list type="bullet">
/// <item>inside a string literal the value is JSON-escaped text (a body node that isn't a string becomes its JSON text);</item>
/// <item>outside one a body node goes in as JSON as-is, and any other value as a bare JSON number/true/false/null if it reads as one, otherwise as a quoted string;</item>
/// <item>an unresolvable placeholder becomes <c>null</c> outside a string and an empty string inside one, plus a warning.</item>
/// </list>
/// Any other template gets plain text substitution (unresolved → empty). <c>{{…}}</c> that isn't
/// <c>uuid</c>, <c>now</c> or <c>request.…</c> is left alone — it's the example's own text.
/// </summary>
public sealed class ResponseTemplateEngine(TimeProvider timeProvider) : IResponseTemplateEngine
{
    private const string BodyPrefix = "request.body";

    // The mock answers JSON over HTTP, not HTML — no need to \u-escape non-ASCII or <>&.
    private static readonly JsonSerializerOptions StringEscapeOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public TemplateRenderResult Render(string template, TemplateContext context)
    {
        if (!template.Contains("{{", StringComparison.Ordinal))
        {
            return new TemplateRenderResult(template, []);
        }

        var renderer = new Renderer(template, context, timeProvider);
        return new TemplateRenderResult(renderer.Run(), renderer.Warnings);
    }

    /// <summary>One render's state: the output, the warnings, and the request body parsed at most once.</summary>
    private sealed class Renderer(string template, TemplateContext context, TimeProvider timeProvider)
    {
        private readonly StringBuilder _output = new(template.Length);
        private bool _bodyParsed;
        private JsonNode? _body;
        private string? _bodyProblem;

        public List<string> Warnings { get; } = [];

        public string Run()
        {
            var jsonMode = template.TrimStart() is [ '{' or '[' or '"', .. ];
            var inString = false;
            var i = 0;
            while (i < template.Length)
            {
                if (template[i] == '{' && i + 1 < template.Length && template[i + 1] == '{')
                {
                    var end = template.IndexOf("}}", i + 2, StringComparison.Ordinal);
                    var expression = end < 0 ? null : template[(i + 2)..end].Trim();
                    if (expression is not null && IsPlaceholder(expression))
                    {
                        Emit(expression, Resolve(expression), jsonMode, inString);
                        i = end + 2;
                        continue;
                    }
                }

                var c = template[i];
                if (jsonMode && inString && c == '\\' && i + 1 < template.Length)
                {
                    _output.Append(c).Append(template[i + 1]);
                    i += 2;
                    continue;
                }

                if (jsonMode && c == '"')
                {
                    inString = !inString;
                }

                _output.Append(c);
                i++;
            }

            return _output.ToString();
        }

        private static bool IsPlaceholder(string expression)
            => expression is "uuid" or "now" || expression.StartsWith("request.", StringComparison.Ordinal);

        private void Emit(string expression, Value value, bool jsonMode, bool inString)
        {
            if (!value.Found)
            {
                Warnings.Add($"{{{{{expression}}}}} couldn't be filled in: {value.Problem}");
                _output.Append(jsonMode && !inString ? "null" : string.Empty);
                return;
            }

            if (!jsonMode || inString)
            {
                var text = value.IsNode ? TextOf(value.Node) : value.Text!;
                _output.Append(jsonMode ? Escape(text) : text);
                return;
            }

            if (value.IsNode)
            {
                _output.Append(value.Node?.ToJsonString() ?? "null");
            }
            else
            {
                _output.Append(IsJsonScalar(value.Text!) ? value.Text : JsonSerializer.Serialize(value.Text, StringEscapeOptions));
            }
        }

        private Value Resolve(string expression)
        {
            switch (expression)
            {
                case "uuid":
                    return Value.OfText(Guid.NewGuid().ToString());
                case "now":
                    return Value.OfText(timeProvider.GetUtcNow().ToString("O", CultureInfo.InvariantCulture));
            }

            if (expression.StartsWith(BodyPrefix, StringComparison.Ordinal))
            {
                var rest = expression[BodyPrefix.Length..];
                if (rest.Length == 0)
                {
                    return ResolveBody("$");
                }

                if (rest[0] == '.' && rest.Length > 1)
                {
                    var path = rest[1..];
                    return ResolveBody(path.StartsWith('$') ? path : "$." + path);
                }
            }

            var parts = expression.Split('.', 3);
            if (parts.Length == 3 && parts[2].Length > 0)
            {
                var name = parts[2];
                switch (parts[1])
                {
                    case "path":
                        return Lookup(context.PathParameters, name, StringComparison.Ordinal, $"the operation's path has no \"{{{name}}}\" parameter");
                    case "query":
                        return Lookup(context.QueryParameters, name, StringComparison.Ordinal, $"the request has no \"{name}\" query parameter");
                    case "header":
                        return Lookup(context.Headers, name, StringComparison.OrdinalIgnoreCase, $"the request has no \"{name}\" header");
                }
            }

            return Value.Missing("unknown placeholder — use request.path.*, request.query.*, request.header.*, request.body.*, uuid or now");
        }

        private static Value Lookup(IReadOnlyDictionary<string, string> values, string name, StringComparison comparison, string problem)
        {
            if (values.TryGetValue(name, out var exact))
            {
                return Value.OfText(exact);
            }

            foreach (var (key, value) in values)
            {
                if (string.Equals(key, name, comparison))
                {
                    return Value.OfText(value);
                }
            }

            return Value.Missing(problem);
        }

        private Value ResolveBody(string path)
        {
            if (!JsonPath.TryParse(path, out var jsonPath))
            {
                return Value.Missing($"\"{path}\" isn't a valid JSON path");
            }

            var body = ParsedBody();
            if (_bodyProblem is not null)
            {
                return Value.Missing(_bodyProblem);
            }

            var matches = jsonPath.Evaluate(body).Matches;
            return matches switch
            {
                null or { Count: 0 } => Value.Missing($"nothing in the request body matches \"{path}\""),
                { Count: 1 } => Value.OfNode(matches[0].Value),
                _ => Value.OfNode(new JsonArray(matches.Select(match => match.Value?.DeepClone()).ToArray()))
            };
        }

        private JsonNode? ParsedBody()
        {
            if (_bodyParsed)
            {
                return _body;
            }

            _bodyParsed = true;
            if (string.IsNullOrWhiteSpace(context.Body))
            {
                _bodyProblem = "the request has no body";
                return null;
            }

            try
            {
                _body = JsonNode.Parse(context.Body);
            }
            catch (JsonException)
            {
                _bodyProblem = "the request body isn't JSON";
            }

            return _body;
        }

        /// <summary>A string node's own text; any other node's JSON text.</summary>
        private static string TextOf(JsonNode? node)
            => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : node?.ToJsonString() ?? "null";

        /// <summary>JSON-escapes <paramref name="text"/> for use inside an existing string literal (without adding quotes).</summary>
        private static string Escape(string text) => JsonSerializer.Serialize(text, StringEscapeOptions)[1..^1];

        private static bool IsJsonScalar(string text)
        {
            if (text is "true" or "false" or "null")
            {
                return true;
            }

            try
            {
                return JsonNode.Parse(text) is JsonValue value && value.GetValueKind() == JsonValueKind.Number;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }

    /// <summary>A resolved placeholder: request text (path/query/header/uuid/now), a body node, or why it couldn't be resolved.</summary>
    private readonly record struct Value(bool Found, string? Text, JsonNode? Node, bool IsNode, string? Problem)
    {
        public static Value OfText(string text) => new(true, text, null, false, null);

        public static Value OfNode(JsonNode? node) => new(true, null, node, true, null);

        public static Value Missing(string problem) => new(false, null, null, false, problem);
    }
}
