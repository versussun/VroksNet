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

    [Theory]
    [InlineData("POST /pets", 201)] // the response the example came from
    [InlineData("GET /pets", 200)] // no example: the lowest declared 2xx
    [InlineData("PUT /pets/{id}", 200)] // example from the request body: lowest 2xx, "2XX" counting as 200
    [InlineData("DELETE /pets/{id}", 204)]
    [InlineData("GET /health", null)] // only "default"
    public async Task ParseAsync_ExampleStatuses_RecordsTheStatusTheMockAnswersWith(string operationKey, int? expected)
    {
        var yaml = await File.ReadAllTextAsync(FixturePath("example-statuses-openapi.yaml"), TestContext.Current.CancellationToken);

        var result = await _parser.ParseAsync(yaml, TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Operations.Single(operation => operation.OperationKey == operationKey).ExampleStatusCode);
    }

    [Fact]
    public async Task ParseAsync_PetstoreSample_ExtractsResponseExampleAsIndentedJson()
    {
        var yaml = await File.ReadAllTextAsync(FixturePath("petstore-openapi.yaml"), TestContext.Current.CancellationToken);

        var result = await _parser.ParseAsync(yaml, TestContext.Current.CancellationToken);

        var getPetById = result.Operations.Single(operation => operation.OperationKey == "GET /pets/{petId}");
        Assert.NotNull(getPetById.ExampleJson);
        Assert.False(getPetById.ExampleIsGenerated);
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

    [Fact]
    public async Task ParseAsync_NoExample_BuildsOneFromTheSchemaOfTheStatusTheMockAnswersWith()
    {
        var operation = await ParseNoExamplesAsync("GET /orders/{id}");

        // The 200's schema (not the 404's), through the components' $refs; formats become placeholders.
        Assert.Equal((200, true), (operation.ExampleStatusCode, operation.ExampleIsGenerated));
        var example = System.Text.Json.Nodes.JsonNode.Parse(operation.ExampleJson!)!;
        Assert.Equal(("{{uuid}}", "placed", "{{now}}", "EUR"), ((string)example["id"]!, (string)example["status"]!, (string)example["placedAt"]!, (string)example["total"]!["currency"]!));
        Assert.True((double)example["total"]!["amount"]! > 0); // OpenAPI 3.0's exclusiveMinimum flag
        Assert.Null(example["error"]);
    }

    [Fact]
    public async Task ParseAsync_NoExample_ANoBodyStatus_FallsBackToTheRequestBodySchema()
    {
        var operation = await ParseNoExamplesAsync("POST /orders");

        Assert.Equal(202, operation.ExampleStatusCode);
        Assert.Contains("\"quantity\": 1", operation.ExampleJson);
        Assert.DoesNotContain("error", operation.ExampleJson); // the declared 202 wins over "default", body or not
    }

    [Fact]
    public async Task ParseAsync_NoExample_ARangeKey_AndARecursiveSchema()
    {
        var operation = await ParseNoExamplesAsync("GET /categories");

        var category = System.Text.Json.Nodes.JsonNode.Parse(operation.ExampleJson!)!.AsArray().Single()!;
        Assert.Equal("string", (string)category["name"]!);
    }

    [Theory]
    [InlineData("GET /orders/{id}")]
    [InlineData("POST /orders")]
    [InlineData("GET /categories")]
    public async Task ParseAsync_NoExample_TheBuiltOneMatchesTheSchemaTheMockValidatesAgainst(string operationKey)
    {
        var operation = await ParseNoExamplesAsync(operationKey);
        var schema = operation.ResponseSchemasByStatus!.GetValueOrDefault(operation.ExampleStatusCode!.Value.ToString())
            ?? operation.ResponseSchemasByStatus!.GetValueOrDefault("2XX")
            ?? operation.RequestSchemaJson!;
        var rendered = new VroksNet.Infrastructure.Templating.ResponseTemplateEngine(TimeProvider.System)
            .Render(operation.ExampleJson!, new(new Dictionary<string, string>(), new Dictionary<string, string>(), new Dictionary<string, string>(), null));

        var validation = new SchemaValidator().Validate(schema, rendered.Text);

        Assert.True(validation.IsValid, string.Join("\n", validation.Errors));
    }

    private async Task<VroksNet.Application.Abstractions.ParsedOperation> ParseNoExamplesAsync(string operationKey)
    {
        var yaml = await File.ReadAllTextAsync(FixturePath("no-examples-openapi.yaml"), TestContext.Current.CancellationToken);
        var result = await _parser.ParseAsync(yaml, TestContext.Current.CancellationToken);
        return result.Operations.Single(operation => operation.OperationKey == operationKey);
    }

    private static string FixturePath(string fileName) => Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
}
