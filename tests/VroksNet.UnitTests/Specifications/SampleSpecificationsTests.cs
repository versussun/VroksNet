using VroksNet.Domain.Connections;
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
public sealed class SampleSpecificationsTests
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
            AssertValid(operation, schema);
        }
    }

    [Theory]
    [InlineData("orders-asyncapi.yaml", "Orders Sample Events", 1)]
    [InlineData("shop-events-kafka-asyncapi.yaml", "Shop Events Kafka Sample", 5)]
    [InlineData("iot-telemetry-nats-asyncapi.yaml", "IoT Telemetry NATS Sample", 5)]
    [InlineData("home-sensors-mqtt-asyncapi.yaml", "Home Sensors MQTT Sample", 3)]
    [InlineData("notifications-rabbitmq-asyncapi.yaml", "Notifications RabbitMQ Sample", 4)]
    public async Task AsyncApiSample_EveryOperationHasAnExampleThatMatchesItsPayloadSchema(string fileName, string title, int operationCount)
    {
        var spec = await new AsyncApiSpecificationParser().ParseAsync(await ReadSampleAsync(fileName), TestContext.Current.CancellationToken);

        Assert.Equal(title, spec.Title);
        Assert.Equal(operationCount, spec.Operations.Count);
        foreach (var operation in spec.Operations)
        {
            Assert.True(TestScenarioListening.CanListen(operation.OperationKey), $"{operation.OperationKey}: can't be listened to.");
            AssertValid(operation, operation.ResponseSchemaJson);
        }
    }

    /// <summary>R5: the servers' protocols, which decide which connections a form offers first.</summary>
    [Theory]
    [InlineData("shop-events-kafka-asyncapi.yaml", new[] { "kafka", "kafka-secure" }, new[] { ConnectionServiceType.Kafka })]
    [InlineData("iot-telemetry-nats-asyncapi.yaml", new[] { "nats" }, new[] { ConnectionServiceType.Nats })]
    [InlineData("home-sensors-mqtt-asyncapi.yaml", new[] { "mqtt" }, new[] { ConnectionServiceType.Mqtt })]
    [InlineData("notifications-rabbitmq-asyncapi.yaml", new[] { "amqp" }, new[] { ConnectionServiceType.RabbitMq })]
    public async Task AsyncApiSample_ServerProtocols_PointAtItsBroker(string fileName, string[] protocols, ConnectionServiceType[] types)
    {
        var spec = await new AsyncApiSpecificationParser().ParseAsync(await ReadSampleAsync(fileName), TestContext.Current.CancellationToken);

        Assert.Equal(protocols, spec.Protocols);
        Assert.Equal(types, ServiceTypeTraits.TypesFor(spec.Protocols!));
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

    /// <summary>Fails if the operation has no example, <paramref name="schemaJson"/> is missing, or the example doesn't match it.</summary>
    private void AssertValid(ParsedOperation operation, string? schemaJson)
    {
        var example = operation.ExampleJson;
        if (example is null || schemaJson is null)
        {
            Assert.Fail($"{operation.OperationKey}: {(example is null ? "no example" : $"no schema for its example (status {operation.ExampleStatusCode})")}.");
        }

        var result = _validator.Validate(schemaJson, example);
        Assert.True(result.IsValid, $"{operation.OperationKey}: example doesn't match its schema — {string.Join("; ", result.Errors)}");
    }

    private static Task<string> ReadSampleAsync(string fileName)
        => File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Samples", fileName), TestContext.Current.CancellationToken);
}
