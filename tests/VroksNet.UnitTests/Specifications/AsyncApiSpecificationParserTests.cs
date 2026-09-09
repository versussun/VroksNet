using VroksNet.Infrastructure.Specifications;

namespace VroksNet.UnitTests.Specifications;

public class AsyncApiSpecificationParserTests
{
    private readonly AsyncApiSpecificationParser _parser = new();

    [Fact]
    public async Task ParseAsync_OrdersSample_ReturnsTitleAndFlatOperationList()
    {
        var yaml = await File.ReadAllTextAsync(FixturePath("orders-asyncapi.yaml"), TestContext.Current.CancellationToken);

        var result = await _parser.ParseAsync(yaml, TestContext.Current.CancellationToken);

        Assert.Equal("Orders Events Sample API", result.Title);
        Assert.Equal(
            new[] { "orders.created:send", "orders.shipped:receive" },
            result.Operations.Select(operation => operation.OperationKey));
    }

    [Fact]
    public async Task ParseAsync_OrdersSample_ExtractsMessageExampleAsIndentedJson()
    {
        var yaml = await File.ReadAllTextAsync(FixturePath("orders-asyncapi.yaml"), TestContext.Current.CancellationToken);

        var result = await _parser.ParseAsync(yaml, TestContext.Current.CancellationToken);

        var orderCreated = result.Operations.Single(operation => operation.OperationKey == "orders.created:send");
        Assert.NotNull(orderCreated.ExampleJson);
        Assert.Contains("\"orderId\": \"ord_1\"", orderCreated.ExampleJson);
        Assert.Contains("\"amount\": 42.5", orderCreated.ExampleJson);
    }

    [Fact]
    public async Task ParseAsync_MalformedYaml_Throws()
    {
        var yaml = await File.ReadAllTextAsync(FixturePath("invalid-asyncapi.yaml"), TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _parser.ParseAsync(yaml, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ParseAsync_NotAnAsyncApiDocument_Throws()
    {
        var yaml = await File.ReadAllTextAsync(FixturePath("petstore-openapi.yaml"), TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _parser.ParseAsync(yaml, TestContext.Current.CancellationToken));
    }

    private static string FixturePath(string fileName) => Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
}
