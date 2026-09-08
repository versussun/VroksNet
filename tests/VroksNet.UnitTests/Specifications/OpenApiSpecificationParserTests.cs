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
    public async Task ParseAsync_MalformedYaml_Throws()
    {
        var yaml = await File.ReadAllTextAsync(FixturePath("invalid-openapi.yaml"), TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _parser.ParseAsync(yaml, TestContext.Current.CancellationToken));
    }

    private static string FixturePath(string fileName) => Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
}
