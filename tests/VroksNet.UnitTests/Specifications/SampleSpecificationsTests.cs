using VroksNet.Application.Abstractions;
using VroksNet.Domain.TestScenarios;
using VroksNet.Infrastructure.SchemaValidation;
using VroksNet.Infrastructure.Specifications;

namespace VroksNet.UnitTests.Specifications;

/// <summary>
/// Keeps docs/samples importable: every sample parses into the operations its README promises,
/// and each operation's example passes its own schema — so a contract test or Listen run against
/// a mock built from a sample starts out green. Placeholders like "{{uuid}}" sit in plain string
/// fields, which is why the raw example can be validated without rendering it.
/// </summary>
public class SampleSpecificationsTests
{
    private readonly SchemaValidator _validator = new();

    [Theory]
    [InlineData("petstore-openapi.yaml", "Petstore Sample API", 3)]
    [InlineData("bookstore-openapi.yaml", "Bookstore Sample API", 10)]
    [InlineData("payments-openapi-3.1.yaml", "Payments Sample API", 4)]
    [InlineData("inventory-swagger-2.0.yaml", "Inventory Sample API", 4)]
    public async Task OpenApiSample_ParsesAndItsResponseExamplesMatchTheirSchemas(string fileName, string title, int operationCount)
    {
        var spec = await new OpenApiSpecificationParser().ParseAsync(await ReadSampleAsync(fileName), TestContext.Current.CancellationToken);

        Assert.Equal(title, spec.Title);
        Assert.Equal(operationCount, spec.Operations.Count);
        foreach (var operation in spec.Operations.Where(operation => operation.ExampleJson is not null))
        {
            var schema = operation.ResponseSchemasByStatus?.GetValueOrDefault(operation.ExampleStatusCode?.ToString() ?? string.Empty);
            Assert.True(schema is not null, $"{operation.OperationKey}: no response schema for its example's status {operation.ExampleStatusCode}.");
            AssertValid(operation, schema);
        }
    }

    [Theory]
    [InlineData("orders-asyncapi.yaml", "Orders Sample Events", 1)]
    [InlineData("shop-events-kafka-asyncapi.yaml", "Shop Events Kafka Sample", 5)]
    [InlineData("iot-telemetry-nats-asyncapi.yaml", "IoT Telemetry NATS Sample", 5)]
    [InlineData("notifications-rabbitmq-asyncapi.yaml", "Notifications RabbitMQ Sample", 4)]
    public async Task AsyncApiSample_EveryOperationHasAnExampleThatMatchesItsPayloadSchema(string fileName, string title, int operationCount)
    {
        var spec = await new AsyncApiSpecificationParser().ParseAsync(await ReadSampleAsync(fileName), TestContext.Current.CancellationToken);

        Assert.Equal(title, spec.Title);
        Assert.Equal(operationCount, spec.Operations.Count);
        foreach (var operation in spec.Operations)
        {
            Assert.True(operation.ExampleJson is not null, $"{operation.OperationKey}: no example to publish.");
            Assert.True(operation.ResponseSchemaJson is not null, $"{operation.OperationKey}: no payload schema.");
            Assert.True(TestScenarioListening.CanListen(operation.OperationKey), $"{operation.OperationKey}: can't be listened to.");
            AssertValid(operation, operation.ResponseSchemaJson);
        }
    }

    [Fact]
    public async Task OpenApiSample_TheBookstoreMockAnswersWithTheStatusItsExampleCameFrom()
    {
        var spec = await new OpenApiSpecificationParser().ParseAsync(await ReadSampleAsync("bookstore-openapi.yaml"), TestContext.Current.CancellationToken);

        Assert.Equal(201, spec.Operations.Single(operation => operation.OperationKey == "POST /books").ExampleStatusCode);
        Assert.Equal(202, spec.Operations.Single(operation => operation.OperationKey == "POST /orders").ExampleStatusCode);
        Assert.Equal(204, spec.Operations.Single(operation => operation.OperationKey == "DELETE /books/{bookId}").ExampleStatusCode);
    }

    [Fact]
    public async Task SwaggerSample_ResponseExamplesSurviveTheUpgradeTo3x()
    {
        var spec = await new OpenApiSpecificationParser().ParseAsync(await ReadSampleAsync("inventory-swagger-2.0.yaml"), TestContext.Current.CancellationToken);

        var getStock = spec.Operations.Single(operation => operation.OperationKey == "GET /warehouses/{code}/stock");
        Assert.Contains("{{request.path.code}}", getStock.ExampleJson);
        Assert.NotNull(spec.Operations.Single(operation => operation.OperationKey == "POST /reservations").RequestSchemaJson);
    }

    private void AssertValid(ParsedOperation operation, string? schemaJson)
    {
        var result = _validator.Validate(schemaJson!, operation.ExampleJson!);
        Assert.True(result.IsValid, $"{operation.OperationKey}: example doesn't match its schema — {string.Join("; ", result.Errors)}");
    }

    private static Task<string> ReadSampleAsync(string fileName)
        => File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Samples", fileName), TestContext.Current.CancellationToken);
}
