using VroksNet.Infrastructure.SchemaValidation;
using VroksNet.Infrastructure.Specifications;

namespace VroksNet.UnitTests.Specifications;

public class OpenApiSpecificationParserTests
{
    private readonly OpenApiSpecificationParser _parser = new();

    [Fact]
    public async Task ParseAsync_PetstoreSample_ReturnsTitleAndFlatOperationList()
    {
        var yaml = await File.ReadAllTextAsync(FixturePath("petstore-openapi.yaml"), TestContext.Current.CancellationToken);

        var result = await _parser.ParseAsync(yaml, TestContext.Current.CancellationToken);

        Assert.Equal("Petstore Sample API", result.Title);
        Assert.Equal(
            new[] { "GET /pets", "POST /pets", "GET /pets/{petId}" },
            result.Operations.Select(operation => operation.OperationKey));
    }

    [Fact]
    public async Task ParseAsync_PetstoreSample_ExtractsResponseExampleAsIndentedJson()
    {
        var yaml = await File.ReadAllTextAsync(FixturePath("petstore-openapi.yaml"), TestContext.Current.CancellationToken);

        var result = await _parser.ParseAsync(yaml, TestContext.Current.CancellationToken);

        var getPetById = result.Operations.Single(operation => operation.OperationKey == "GET /pets/{petId}");
        Assert.NotNull(getPetById.ExampleJson);
        Assert.Contains("\"name\": \"Fido\"", getPetById.ExampleJson);
    }

    [Fact]
    public async Task ParseAsync_PetstoreSample_ExtractsRequestAndResponseSchemasWithRefsInlined()
    {
        var yaml = await File.ReadAllTextAsync(FixturePath("petstore-openapi.yaml"), TestContext.Current.CancellationToken);

        var result = await _parser.ParseAsync(yaml, TestContext.Current.CancellationToken);

        var createPet = result.Operations.Single(operation => operation.OperationKey == "POST /pets");
        Assert.NotNull(createPet.RequestSchemaJson);
        Assert.DoesNotContain("$ref", createPet.RequestSchemaJson);
        Assert.Contains("\"name\"", createPet.RequestSchemaJson);

        Assert.NotNull(createPet.ResponseSchemaJson);
        Assert.DoesNotContain("$ref", createPet.ResponseSchemaJson);
        Assert.Contains("\"id\"", createPet.ResponseSchemaJson);

        // GET /pets has no request body.
        var listPets = result.Operations.Single(operation => operation.OperationKey == "GET /pets");
        Assert.Null(listPets.RequestSchemaJson);
        Assert.NotNull(listPets.ResponseSchemaJson);
    }

    [Fact]
    public async Task ParseAsync_ExtractsEveryDeclaredResponseSchemaKeyedByStatus()
    {
        var yaml = await File.ReadAllTextAsync(FixturePath("response-statuses-openapi.yaml"), TestContext.Current.CancellationToken);

        var result = await _parser.ParseAsync(yaml, TestContext.Current.CancellationToken);

        var schemas = Assert.Single(result.Operations).ResponseSchemasByStatus;
        Assert.NotNull(schemas);
        Assert.Equal(new[] { "200", "404", "5XX", "default" }, schemas.Keys.Order(StringComparer.Ordinal));

        Assert.NotNull(schemas["200"]);
        Assert.DoesNotContain("$ref", schemas["200"]);
        Assert.Contains("\"id\"", schemas["200"]);
        Assert.Contains("\"message\"", schemas["404"]);

        // Declared, but with no JSON body — the key is kept so the status counts as declared.
        Assert.Null(schemas["5XX"]);
        Assert.Null(schemas["default"]);
    }

    [Fact]
    public async Task ParseAsync_RecursiveSchema_CarriesComponentsAsDefsSoItValidatesStandalone()
    {
        var yaml = await File.ReadAllTextAsync(FixturePath("recursive-openapi.yaml"), TestContext.Current.CancellationToken);

        var result = await _parser.ParseAsync(yaml, TestContext.Current.CancellationToken);

        var schema = Assert.Single(result.Operations).ResponseSchemasByStatus!["200"];
        Assert.NotNull(schema);
        Assert.DoesNotContain("#/components/", schema);
        Assert.Contains("\"$defs\"", schema);

        var validator = new SchemaValidator();
        Assert.True(validator.Validate(schema, """{"name":"root","children":[{"name":"leaf","children":[]}]}""").IsValid);
        // The violation is two levels deep — past the one level Microsoft.OpenApi inlines.
        Assert.False(validator.Validate(schema, """{"name":"root","children":[{"name":"a","children":[{"children":[]}]}]}""").IsValid);
    }

    [Fact]
    public async Task ParseAsync_MalformedYaml_Throws()
    {
        var yaml = await File.ReadAllTextAsync(FixturePath("invalid-openapi.yaml"), TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _parser.ParseAsync(yaml, TestContext.Current.CancellationToken));
    }

    private static string FixturePath(string fileName) => Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
}
