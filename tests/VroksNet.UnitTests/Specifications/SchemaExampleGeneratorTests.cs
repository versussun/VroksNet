using System.Text.Json.Nodes;
using VroksNet.Application.Abstractions;
using VroksNet.Infrastructure.SchemaValidation;
using VroksNet.Infrastructure.Specifications;
using VroksNet.Infrastructure.Templating;

namespace VroksNet.UnitTests.Specifications;

/// <summary>
/// Examples built from a schema for operations the spec gives none. Each one that should be
/// valid is checked the way the mock serves it: rendered by the real template engine (its
/// {{uuid}}/{{now}} filled in), then validated by the real SchemaValidator.
/// </summary>
public class SchemaExampleGeneratorTests
{
    private static readonly ResponseTemplateEngine TemplateEngine = new(TimeProvider.System);
    private static readonly SchemaValidator Validator = new();

    [Theory]
    [InlineData("""{"type":"object","properties":{"id":{"type":"integer"},"name":{"type":"string"},"active":{"type":"boolean"}}}""")]
    [InlineData("""{"type":"object","required":["id","createdAt"],"properties":{"id":{"type":"string","format":"uuid"},"createdAt":{"type":"string","format":"date-time"}}}""")]
    [InlineData("""{"type":"array","minItems":2,"items":{"type":"object","properties":{"sku":{"type":"string","minLength":12}}}}""")]
    [InlineData("""{"type":"object","properties":{"quantity":{"type":"integer","minimum":1,"maximum":10},"price":{"type":"number","exclusiveMinimum":0},"step":{"type":"integer","minimum":7,"multipleOf":5}}}""")]
    [InlineData("""{"type":"object","properties":{"status":{"type":"string","enum":["placed","paid"]},"kind":{"const":"order"},"note":{"type":["null","string"]}}}""")]
    [InlineData("""{"allOf":[{"type":"object","properties":{"id":{"type":"integer"}}},{"type":"object","properties":{"name":{"type":"string"}}}]}""")]
    [InlineData("""{"oneOf":[{"type":"null"},{"type":"object","properties":{"id":{"type":"integer"}}}]}""")]
    [InlineData("""{"$defs":{"Money":{"type":"object","properties":{"amount":{"type":"number","minimum":0},"currency":{"type":"string","maxLength":3,"minLength":3}}}},"type":"object","properties":{"total":{"$ref":"#/$defs/Money"}}}""")]
    [InlineData("""{"type":"object","properties":{"email":{"type":"string","format":"email"},"site":{"type":"string","format":"uri"},"day":{"type":"string","format":"date"}}}""")]
    [InlineData("""{"type":"string","maxLength":3}""")]
    [InlineData("""{"type":"integer","exclusiveMaximum":-5}""")]
    public void Generate_MatchesItsOwnSchema(string schema)
    {
        var example = SchemaExampleGenerator.Generate(schema);

        Assert.NotNull(example);
        var rendered = TemplateEngine.Render(example, new TemplateContext(new Dictionary<string, string>(), new Dictionary<string, string>(), new Dictionary<string, string>(), null));
        Assert.Empty(rendered.Warnings);
        var validation = Validator.Validate(schema, rendered.Text);
        Assert.True(validation.IsValid, $"{rendered.Text}\n{string.Join("\n", validation.Errors)}");
    }

    [Fact]
    public void Generate_UsesTheValuesTheSchemaGives_AtAnyLevel()
    {
        var example = Parse(SchemaExampleGenerator.Generate("""
            {"type":"object","properties":{
              "name":{"type":"string","examples":["Fido"]},
              "tag":{"type":"string","example":"dog"},
              "size":{"type":"string","default":"m"},
              "age":{"type":"integer","enum":[3,4]}}}
            """));

        Assert.Equal(("Fido", "dog", "m", 3), ((string)example["name"]!, (string)example["tag"]!, (string)example["size"]!, (int)example["age"]!));
    }

    [Fact]
    public void Generate_UuidAndDateTime_BecomePlaceholders_FilledPerUse()
    {
        var example = Parse(SchemaExampleGenerator.Generate("""{"type":"object","properties":{"id":{"type":"string","format":"uuid"},"at":{"type":"string","format":"date-time"}}}"""));

        Assert.Equal(("{{uuid}}", "{{now}}"), ((string)example["id"]!, (string)example["at"]!));
    }

    [Fact]
    public void Generate_RecursiveSchema_StopsAtTheCycle()
    {
        var example = Parse(SchemaExampleGenerator.Generate("""
            {"$defs":{"Node":{"type":"object","properties":{"name":{"type":"string"},"children":{"type":"array","items":{"$ref":"#/$defs/Node"}}}}},
             "$ref":"#/$defs/Node"}
            """));

        // The root Node is built; its children's Node is the same $ref again, so the array stays empty.
        Assert.Equal("string", (string)example["name"]!);
        Assert.Empty(example["children"]!.AsArray());
    }

    [Fact]
    public void Generate_ARefTheSchemaDoesntHold_GoesThroughTheResolver()
    {
        var example = Parse(SchemaExampleGenerator.Generate(
            """{"type":"object","properties":{"total":{"$ref":"#/components/schemas/Money"}}}""",
            reference => reference == "#/components/schemas/Money" ? JsonNode.Parse("""{"type":"object","properties":{"currency":{"type":"string","example":"EUR"}}}""") : null));

        Assert.Equal("EUR", (string)example["total"]!["currency"]!);
    }

    [Fact]
    public void Generate_AnUnresolvableRef_LeavesThePropertyOut()
    {
        var example = Parse(SchemaExampleGenerator.Generate("""{"type":"object","properties":{"id":{"type":"integer"},"other":{"$ref":"#/components/schemas/Missing"}}}"""));

        Assert.Equal(["id"], example.Select(property => property.Key));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("true")]  // "anything" — nothing to build from
    [InlineData("{}")]
    public void Generate_NothingToBuildFrom_IsNull(string? schema)
        => Assert.Null(SchemaExampleGenerator.Generate(schema));

    [Fact]
    public void Generate_IsIndentedJson_LikeASpecsOwnExample()
        => Assert.Equal("{\n  \"id\": 0\n}", SchemaExampleGenerator.Generate("""{"type":"object","properties":{"id":{"type":"integer"}}}""")!.ReplaceLineEndings("\n"));

    private static JsonObject Parse(string? example)
    {
        Assert.NotNull(example);
        return JsonNode.Parse(example)!.AsObject();
    }
}
