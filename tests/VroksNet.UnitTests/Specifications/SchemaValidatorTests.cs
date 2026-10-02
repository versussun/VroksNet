using VroksNet.Infrastructure.SchemaValidation;

namespace VroksNet.UnitTests.Specifications;

public class SchemaValidatorTests
{
    private readonly SchemaValidator _validator = new();

    private const string PersonSchema = """
        {
          "type": "object",
          "required": ["name"],
          "properties": {
            "name": { "type": "string" },
            "age": { "type": "integer", "minimum": 0 }
          }
        }
        """;

    [Fact]
    public void Validate_MatchingInstance_ReturnsValid()
    {
        var result = _validator.Validate(PersonSchema, """{"name":"Fido","age":3}""");

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_MissingRequiredProperty_ReturnsErrors()
    {
        var result = _validator.Validate(PersonSchema, """{"age":3}""");

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Validate_WrongPropertyType_ReturnsErrors()
    {
        var result = _validator.Validate(PersonSchema, """{"name":"Fido","age":"not a number"}""");

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Validate_ValueBelowMinimum_ReturnsErrors()
    {
        var result = _validator.Validate(PersonSchema, """{"name":"Fido","age":-1}""");

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Validate_MalformedInstanceJson_ReturnsError()
    {
        var result = _validator.Validate(PersonSchema, "{not json");

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Validate_UnresolvableRef_ReturnsErrorInsteadOfThrowing()
    {
        const string schema = """{ "type": "object", "properties": { "child": { "$ref": "#/components/schemas/Missing" } } }""";

        var result = _validator.Validate(schema, """{"child":{}}""");

        Assert.False(result.IsValid);
        Assert.Contains("couldn't be evaluated", Assert.Single(result.Errors));
    }
}
