using System.Text.Json.Nodes;
using VroksNet.Application.Abstractions;
using VroksNet.Infrastructure.Templating;
using VroksNet.UnitTests.TestDoubles;

namespace VroksNet.UnitTests.Templating;

public class ResponseTemplateEngineTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 30, 0, TimeSpan.Zero);

    private readonly ResponseTemplateEngine _engine = new(new FixedTimeProvider(Now));

    [Fact]
    public void Render_NoPlaceholders_ReturnsTemplateAsIs()
    {
        var result = _engine.Render("""{"id": 1}""", Context());

        Assert.Equal("""{"id": 1}""", result.Text);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Render_PathQueryAndHeaderInsideStrings_SubstitutesText()
    {
        var context = Context(
            path: new() { ["id"] = "42" },
            query: new() { ["lang"] = "uk" },
            headers: new() { ["X-Request-Id"] = "abc" });

        var result = _engine.Render(
            """{"id": "{{request.path.id}}", "lang": "{{ request.query.lang }}", "trace": "req-{{request.header.x-request-id}}"}""",
            context);

        Assert.Equal("""{"id": "42", "lang": "uk", "trace": "req-abc"}""", result.Text);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Render_ValueInsideString_IsJsonEscaped()
    {
        var result = _engine.Render("""{"name": "{{request.query.name}}"}""", Context(query: new() { ["name"] = "say \"hi\"\n" }));

        Assert.Equal("""{"name": "say \"hi\"\n"}""", result.Text);
        Assert.Equal("say \"hi\"\n", JsonNode.Parse(result.Text)!["name"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("42", "42")]
    [InlineData("-1.5e3", "-1.5e3")]
    [InlineData("true", "true")]
    [InlineData("null", "null")]
    [InlineData("Fido", "\"Fido\"")]
    [InlineData("007x", "\"007x\"")]
    public void Render_ValueOutsideString_IsBareScalarOrQuotedString(string value, string expected)
    {
        var result = _engine.Render("""{"value": {{request.path.v}}}""", Context(path: new() { ["v"] = value }));

        Assert.Equal($$"""{"value": {{expected}}}""", result.Text);
        Assert.NotNull(JsonNode.Parse(result.Text));
    }

    [Fact]
    public void Render_BodyNodeOutsideString_IsInsertedAsJson()
    {
        var body = """{"owner": {"name": "Ann", "tags": ["a", "b"]}, "count": 3}""";

        var result = _engine.Render(
            """{"owner": {{request.body.$.owner}}, "count": {{request.body.count}}, "first": {{request.body.owner.tags[0]}}}""",
            Context(body: body));

        Assert.Equal("""{"owner": {"name":"Ann","tags":["a","b"]}, "count": 3, "first": "a"}""", result.Text);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Render_BodyNodeInsideString_UsesStringValueOrJsonText()
    {
        var result = _engine.Render(
            """{"greeting": "Hello, {{request.body.$.name}}", "raw": "{{request.body.$.n}}"}""",
            Context(body: """{"name": "Ann", "n": {"a": 1}}"""));

        Assert.Equal("""{"greeting": "Hello, Ann", "raw": "{\"a\":1}"}""", result.Text);
    }

    [Fact]
    public void Render_WholeBodyAndMultipleMatches()
    {
        var result = _engine.Render(
            """{"echo": {{request.body}}, "names": {{request.body.$.pets[*].name}}}""",
            Context(body: """{"pets": [{"name": "A"}, {"name": "B"}]}"""));

        var rendered = JsonNode.Parse(result.Text)!;
        Assert.Equal("A", rendered["echo"]!["pets"]![0]!["name"]!.GetValue<string>());
        Assert.Equal("""["A","B"]""", rendered["names"]!.ToJsonString());
    }

    [Fact]
    public void Render_UnresolvablePlaceholders_BecomeNullOrEmptyAndWarn()
    {
        var result = _engine.Render(
            """{"a": {{request.path.missing}}, "b": "x{{request.header.nope}}y", "c": {{request.body.$.absent}}, "d": {{request.cookie.x}}}""",
            Context(body: """{"present": 1}"""));

        Assert.Equal("""{"a": null, "b": "xy", "c": null, "d": null}""", result.Text);
        Assert.Equal(4, result.Warnings.Count);
        Assert.Contains(result.Warnings, warning => warning.Contains("{{request.path.missing}}") && warning.Contains("\"{missing}\""));
        Assert.Contains(result.Warnings, warning => warning.Contains("{{request.body.$.absent}}"));
    }

    [Theory]
    [InlineData(null, "the request has no body")]
    [InlineData("not json", "the request body isn't JSON")]
    public void Render_BodyPlaceholderWithoutJsonBody_Warns(string? body, string problem)
    {
        var result = _engine.Render("""{"n": {{request.body.$.n}}}""", Context(body: body));

        Assert.Equal("""{"n": null}""", result.Text);
        Assert.Contains(problem, Assert.Single(result.Warnings));
    }

    [Fact]
    public void Render_UuidAndNow_AreFreshGuidAndIsoTimestamp()
    {
        var result = _engine.Render("""{"id": "{{uuid}}", "other": {{uuid}}, "at": "{{now}}"}""", Context());

        var rendered = JsonNode.Parse(result.Text)!;
        var id = Guid.Parse(rendered["id"]!.GetValue<string>());
        var other = Guid.Parse(rendered["other"]!.GetValue<string>());
        Assert.NotEqual(id, other);
        Assert.Equal(Now, DateTimeOffset.Parse(rendered["at"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Render_UnrelatedBracesAndEscapedQuotes_AreLeftAlone()
    {
        var result = _engine.Render("""{"note": "use {{name}} and \"{{request.path.id}}\""}""", Context(path: new() { ["id"] = "7" }));

        Assert.Equal("""{"note": "use {{name}} and \"7\""}""", result.Text);
    }

    [Fact]
    public void Render_NonJsonTemplate_UsesPlainTextSubstitution()
    {
        var result = _engine.Render("Pet {{request.path.id}} owned by {{request.query.owner}}", Context(path: new() { ["id"] = "7" }));

        Assert.Equal("Pet 7 owned by ", result.Text);
        Assert.Single(result.Warnings);
    }

    private static TemplateContext Context(
        Dictionary<string, string>? path = null,
        Dictionary<string, string>? query = null,
        Dictionary<string, string>? headers = null,
        string? body = null)
        => new(path ?? [], query ?? [], headers ?? [], body);
}
