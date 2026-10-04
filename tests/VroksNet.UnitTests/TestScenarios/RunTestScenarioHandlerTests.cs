using VroksNet.Application.Abstractions;
using VroksNet.Application.TestScenarios.RunTestScenario;
using VroksNet.Domain.ApiSpecifications;
using VroksNet.Domain.CallRecords;
using VroksNet.Domain.Connections;
using VroksNet.Domain.MockEndpoints;
using VroksNet.Domain.TestScenarios;
using VroksNet.Infrastructure.SchemaValidation;
using VroksNet.UnitTests.TestDoubles;

namespace VroksNet.UnitTests.TestScenarios;

public class RunTestScenarioHandlerTests
{
    [Fact]
    public async Task Handle_ExistingScenario_SendsWithResolvedPayloadAndLogsCallRecord()
    {
        var specifications = new FakeApiSpecificationRepository();
        var connections = new FakeConnectionRepository();
        var scenarios = new FakeTestScenarioRepository();
        var callRecords = new FakeCallRecordRepository();
        var sender = new FakeMessageSender(new MessageSendResult(true, "200 OK", "{\"id\":1}"));

        var specificationId = Guid.NewGuid();
        var endpointId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();

        await specifications.UpsertAsync(new ApiSpecification
        {
            Id = specificationId,
            Title = "Petstore",
            Kind = SpecificationKind.OpenApi,
            Endpoints = [new MockEndpoint { Id = endpointId, SpecificationId = specificationId, OperationKey = "GET /pets", ExampleTemplate = "[]" }]
        }, TestContext.Current.CancellationToken);
        await connections.InsertAsync(new Connection { Id = connectionId, Name = "Orders API", ServiceType = ConnectionServiceType.Http, Value = "https://api.example.com" }, TestContext.Current.CancellationToken);

        var scenarioId = Guid.NewGuid();
        await scenarios.InsertAsync(new TestScenario
        {
            Id = scenarioId,
            Name = "Send GET /pets",
            SpecificationId = specificationId,
            MockEndpointId = endpointId,
            ConnectionId = connectionId,
            PayloadOverride = null
        }, TestContext.Current.CancellationToken);

        var handler = TestRunHandlers.Run(scenarios, specifications, connections, sender, new FakeMessageListener(new MessageListenResult(false, "unused")), new SchemaValidator(), callRecords);
        var result = await handler.Handle(new RunTestScenario(scenarioId), TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal("200 OK", result.Message);
        Assert.Equal("{\"id\":1}", result.ResponseBody);

        Assert.NotNull(sender.LastSend);
        Assert.Equal("GET /pets", sender.LastSend.Value.OperationKey);
        Assert.Equal("[]", sender.LastSend.Value.Payload); // falls back to the endpoint's own example

        var logged = Assert.Single(callRecords.Inserted);
        Assert.Equal(specificationId, logged.SpecificationId);
        Assert.Equal(endpointId, logged.MockEndpointId);
        Assert.Equal(connectionId, logged.ConnectionId);
        Assert.Equal(CallDirection.OutboundHttpRequest, logged.Direction);
        Assert.Equal("[]", logged.RequestSnapshot);
        Assert.Equal("{\"id\":1}", logged.ResponseSnapshot);

        var stored = await scenarios.FindByIdAsync(scenarioId, TestContext.Current.CancellationToken);
        Assert.NotNull(stored);
        Assert.NotNull(stored.LastRunAt);
        Assert.True(stored.LastRunSuccess);
        Assert.Equal("200 OK", stored.LastRunMessage);
    }

    [Fact]
    public async Task Handle_PayloadOverride_SendsOverrideInsteadOfExample()
    {
        var specifications = new FakeApiSpecificationRepository();
        var connections = new FakeConnectionRepository();
        var scenarios = new FakeTestScenarioRepository();
        var sender = new FakeMessageSender(new MessageSendResult(true, "Published", null));

        var specificationId = Guid.NewGuid();
        var endpointId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();

        await specifications.UpsertAsync(new ApiSpecification
        {
            Id = specificationId,
            Title = "Orders",
            Kind = SpecificationKind.AsyncApi,
            Endpoints = [new MockEndpoint { Id = endpointId, SpecificationId = specificationId, OperationKey = "orders.created:send", ExampleTemplate = "{\"orderId\":\"default\"}" }]
        }, TestContext.Current.CancellationToken);
        await connections.InsertAsync(new Connection { Id = connectionId, Name = "Broker", ServiceType = ConnectionServiceType.RabbitMq, Value = "amqp://localhost" }, TestContext.Current.CancellationToken);

        var scenarioId = Guid.NewGuid();
        await scenarios.InsertAsync(new TestScenario
        {
            Id = scenarioId,
            Name = "Publish custom order",
            SpecificationId = specificationId,
            MockEndpointId = endpointId,
            ConnectionId = connectionId,
            PayloadOverride = "{\"orderId\":\"custom\"}"
        }, TestContext.Current.CancellationToken);

        var handler = TestRunHandlers.Run(scenarios, specifications, connections, sender, new FakeMessageListener(new MessageListenResult(false, "unused")), new SchemaValidator(), new FakeCallRecordRepository());
        await handler.Handle(new RunTestScenario(scenarioId), TestContext.Current.CancellationToken);

        Assert.Equal("{\"orderId\":\"custom\"}", sender.LastSend!.Value.Payload);
    }

    [Fact]
    public async Task Handle_ExamplePlaceholders_AreFilledInBeforeSending_LikeAPublishers()
    {
        var specifications = new FakeApiSpecificationRepository();
        var connections = new FakeConnectionRepository();
        var scenarios = new FakeTestScenarioRepository();
        var callRecords = new FakeCallRecordRepository();
        var sender = new FakeMessageSender(new MessageSendResult(true, "Published", null));
        var specificationId = Guid.NewGuid();
        var endpointId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        await specifications.UpsertAsync(new ApiSpecification
        {
            Id = specificationId,
            Title = "Orders",
            Kind = SpecificationKind.AsyncApi,
            // What an example built from a schema looks like, plus a placeholder a Send can't fill.
            Endpoints = [new MockEndpoint { Id = endpointId, SpecificationId = specificationId, OperationKey = "orders.created:send", ExampleTemplate = "{\"id\":\"{{uuid}}\",\"at\":\"{{now}}\",\"path\":\"{{request.path.id}}\"}", ExampleIsGenerated = true }]
        }, TestContext.Current.CancellationToken);
        await connections.InsertAsync(new Connection { Id = connectionId, Name = "Broker", ServiceType = ConnectionServiceType.RabbitMq, Value = "amqp://localhost" }, TestContext.Current.CancellationToken);
        var scenarioId = Guid.NewGuid();
        await scenarios.InsertAsync(new TestScenario { Id = scenarioId, Name = "Publish order", SpecificationId = specificationId, MockEndpointId = endpointId, ConnectionId = connectionId }, TestContext.Current.CancellationToken);

        var handler = TestRunHandlers.Run(scenarios, specifications, connections, sender, new FakeMessageListener(new MessageListenResult(false, "unused")), new SchemaValidator(), callRecords);
        await handler.Handle(new RunTestScenario(scenarioId), TestContext.Current.CancellationToken);

        var payload = System.Text.Json.Nodes.JsonNode.Parse(sender.LastSend!.Value.Payload!)!;
        Assert.True(Guid.TryParse((string)payload["id"]!, out _), payload.ToJsonString());
        Assert.True(DateTimeOffset.TryParse((string)payload["at"]!, out _));
        var record = Assert.Single(callRecords.Inserted);
        Assert.Equal(sender.LastSend.Value.Payload, record.RequestSnapshot); // the history holds what was sent
        Assert.Contains("request.path.id", record.Warnings);
    }

    [Fact]
    public async Task Handle_UnknownScenario_ReturnsNull()
    {
        var handler = TestRunHandlers.Run(
            new FakeTestScenarioRepository(), new FakeApiSpecificationRepository(), new FakeConnectionRepository(),
            new FakeMessageSender(new MessageSendResult(true, "unused")), new FakeMessageListener(new MessageListenResult(false, "unused")), new SchemaValidator(), new FakeCallRecordRepository());

        var result = await handler.Handle(new RunTestScenario(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_ScenarioWithDeletedConnection_ReturnsFailureWithoutCallingSenderOrLogging()
    {
        var specifications = new FakeApiSpecificationRepository();
        var connections = new FakeConnectionRepository();
        var scenarios = new FakeTestScenarioRepository();
        var callRecords = new FakeCallRecordRepository();
        var sender = new FakeMessageSender(new MessageSendResult(true, "should not be called"));

        var specificationId = Guid.NewGuid();
        var endpointId = Guid.NewGuid();
        await specifications.UpsertAsync(new ApiSpecification
        {
            Id = specificationId,
            Title = "Petstore",
            Kind = SpecificationKind.OpenApi,
            Endpoints = [new MockEndpoint { Id = endpointId, SpecificationId = specificationId, OperationKey = "GET /pets" }]
        }, TestContext.Current.CancellationToken);

        var scenarioId = Guid.NewGuid();
        await scenarios.InsertAsync(new TestScenario
        {
            Id = scenarioId,
            Name = "Orphaned",
            SpecificationId = specificationId,
            MockEndpointId = endpointId,
            ConnectionId = Guid.NewGuid() // never inserted into `connections`
        }, TestContext.Current.CancellationToken);

        var handler = TestRunHandlers.Run(scenarios, specifications, connections, sender, new FakeMessageListener(new MessageListenResult(false, "unused")), new SchemaValidator(), callRecords);
        var result = await handler.Handle(new RunTestScenario(scenarioId), TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Null(sender.LastSend);
        Assert.Empty(callRecords.Inserted);

        var stored = await scenarios.FindByIdAsync(scenarioId, TestContext.Current.CancellationToken);
        Assert.NotNull(stored);
        Assert.NotNull(stored.LastRunAt);
        Assert.False(stored.LastRunSuccess);
        Assert.Equal(result.Message, stored.LastRunMessage);
    }

    private const string PetSchema = """{ "type": "object", "required": ["id", "name"], "properties": { "id": { "type": "integer" }, "name": { "type": "string" } } }""";

    [Fact]
    public async Task Handle_HttpResponseMatchingDeclaredSchema_SucceedsAndRecordsValidContract()
    {
        var (handler, scenarioId, callRecords, scenarios) = await ArrangeHttpRunAsync(
            new Dictionary<string, string?> { ["200"] = PetSchema },
            new MessageSendResult(true, "200 OK", """{"id":1,"name":"Fido"}""", 200));

        var result = await handler.Handle(new RunTestScenario(scenarioId), TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(200, result.StatusCode);
        Assert.NotNull(result.ContractValidation);
        Assert.True(result.ContractValidation.IsValid);

        var logged = Assert.Single(callRecords.Inserted);
        Assert.Equal(scenarioId, logged.TestScenarioId);
        Assert.Equal(200, logged.StatusCode);
        Assert.True(logged.ContractValid);
        Assert.Null(logged.ValidationErrors);

        var stored = await scenarios.FindByIdAsync(scenarioId, TestContext.Current.CancellationToken);
        Assert.True(stored!.LastRunSuccess);
    }

    [Fact]
    public async Task Handle_HttpResponseViolatingDeclaredSchema_FailsRunAndRecordsErrors()
    {
        var (handler, scenarioId, callRecords, scenarios) = await ArrangeHttpRunAsync(
            new Dictionary<string, string?> { ["200"] = PetSchema },
            new MessageSendResult(true, "200 OK", """{"id":"not-a-number"}""", 200));

        var result = await handler.Handle(new RunTestScenario(scenarioId), TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.StartsWith("200 OK", result.Message);
        Assert.NotNull(result.ContractValidation);
        Assert.False(result.ContractValidation.IsValid);
        Assert.NotEmpty(result.ContractValidation.Errors);
        Assert.Equal("""{"id":"not-a-number"}""", result.ResponseBody);

        var logged = Assert.Single(callRecords.Inserted);
        Assert.False(logged.ContractValid);
        Assert.NotNull(logged.ValidationErrors);
        Assert.StartsWith("[", logged.ValidationErrors);

        var stored = await scenarios.FindByIdAsync(scenarioId, TestContext.Current.CancellationToken);
        Assert.False(stored!.LastRunSuccess);
        Assert.Equal(result.Message, stored.LastRunMessage);
    }

    [Fact]
    public async Task Handle_HttpStatusValidatedAgainstItsOwnSchema_NotTheFirstResponse()
    {
        const string errorSchema = """{ "type": "object", "required": ["message"], "properties": { "message": { "type": "string" } } }""";
        var (handler, scenarioId, _, _) = await ArrangeHttpRunAsync(
            new Dictionary<string, string?> { ["200"] = PetSchema, ["404"] = errorSchema },
            new MessageSendResult(true, "404 NotFound", """{"message":"no such pet"}""", 404));

        var result = await handler.Handle(new RunTestScenario(scenarioId), TestContext.Current.CancellationToken);

        Assert.True(result!.Success);
        Assert.True(result.ContractValidation!.IsValid);
    }

    [Fact]
    public async Task Handle_HttpStatusCoveredOnlyByRangeOrDefault_UsesThatSchema()
    {
        const string errorSchema = """{ "type": "object", "required": ["message"] }""";
        var (handler, scenarioId, _, _) = await ArrangeHttpRunAsync(
            new Dictionary<string, string?> { ["200"] = PetSchema, ["5XX"] = errorSchema, ["default"] = null },
            new MessageSendResult(true, "503 ServiceUnavailable", """{"oops":true}""", 503));

        var result = await handler.Handle(new RunTestScenario(scenarioId), TestContext.Current.CancellationToken);

        // "5XX" wins over "default" — and the body lacks the required "message".
        Assert.False(result!.Success);
        Assert.False(result.ContractValidation!.IsValid);
    }

    [Fact]
    public async Task Handle_HttpStatusNotDeclared_FailsWithUndeclaredStatusError()
    {
        var (handler, scenarioId, _, _) = await ArrangeHttpRunAsync(
            new Dictionary<string, string?> { ["200"] = PetSchema },
            new MessageSendResult(true, "500 InternalServerError", "boom", 500));

        var result = await handler.Handle(new RunTestScenario(scenarioId), TestContext.Current.CancellationToken);

        Assert.False(result!.Success);
        var error = Assert.Single(result.ContractValidation!.Errors);
        Assert.Contains("500", error);
        Assert.Contains("isn't declared", error);
    }

    [Fact]
    public async Task Handle_DeclaredStatusWithoutJsonBody_IsValidWhateverTheBody()
    {
        var (handler, scenarioId, _, _) = await ArrangeHttpRunAsync(
            new Dictionary<string, string?> { ["204"] = null },
            new MessageSendResult(true, "204 NoContent", string.Empty, 204));

        var result = await handler.Handle(new RunTestScenario(scenarioId), TestContext.Current.CancellationToken);

        Assert.True(result!.Success);
        Assert.True(result.ContractValidation!.IsValid);
    }

    [Fact]
    public async Task Handle_EmptyBodyWhereSchemaDeclared_Fails()
    {
        var (handler, scenarioId, _, _) = await ArrangeHttpRunAsync(
            new Dictionary<string, string?> { ["200"] = PetSchema },
            new MessageSendResult(true, "200 OK", string.Empty, 200));

        var result = await handler.Handle(new RunTestScenario(scenarioId), TestContext.Current.CancellationToken);

        Assert.False(result!.Success);
        Assert.Contains("empty", Assert.Single(result.ContractValidation!.Errors));
    }

    [Fact]
    public async Task Handle_NoDeclaredResponses_SkipsValidation()
    {
        // E.g. an operation imported before response schemas were stored.
        var (handler, scenarioId, callRecords, _) = await ArrangeHttpRunAsync(
            [],
            new MessageSendResult(true, "200 OK", "anything", 200));

        var result = await handler.Handle(new RunTestScenario(scenarioId), TestContext.Current.CancellationToken);

        Assert.True(result!.Success);
        Assert.Null(result.ContractValidation);
        Assert.Null(Assert.Single(callRecords.Inserted).ContractValid);
    }

    [Fact]
    public async Task Handle_SendFailed_SkipsValidation()
    {
        var (handler, scenarioId, callRecords, _) = await ArrangeHttpRunAsync(
            new Dictionary<string, string?> { ["200"] = PetSchema },
            new MessageSendResult(false, "Timed out after 10s."));

        var result = await handler.Handle(new RunTestScenario(scenarioId), TestContext.Current.CancellationToken);

        Assert.False(result!.Success);
        Assert.Equal("Timed out after 10s.", result.Message);
        Assert.Null(result.ContractValidation);
        Assert.Null(Assert.Single(callRecords.Inserted).ContractValid);
    }

    /// <summary>One OpenAPI "GET /pets/{id}" operation declaring <paramref name="responseSchemasByStatus"/>, sent through an Http connection whose send returns <paramref name="sendResult"/>.</summary>
    private static async Task<(RunTestScenarioHandler Handler, Guid ScenarioId, FakeCallRecordRepository CallRecords, FakeTestScenarioRepository Scenarios)> ArrangeHttpRunAsync(
        Dictionary<string, string?> responseSchemasByStatus, MessageSendResult sendResult)
    {
        var specifications = new FakeApiSpecificationRepository();
        var connections = new FakeConnectionRepository();
        var scenarios = new FakeTestScenarioRepository();
        var callRecords = new FakeCallRecordRepository();

        var specificationId = Guid.NewGuid();
        var endpointId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var scenarioId = Guid.NewGuid();

        await specifications.UpsertAsync(new ApiSpecification
        {
            Id = specificationId,
            Title = "Petstore",
            Kind = SpecificationKind.OpenApi,
            Endpoints = [new MockEndpoint { Id = endpointId, SpecificationId = specificationId, OperationKey = "GET /pets/{id}", ResponseSchemasByStatus = responseSchemasByStatus }]
        }, TestContext.Current.CancellationToken);
        await connections.InsertAsync(new Connection { Id = connectionId, Name = "Pets API", ServiceType = ConnectionServiceType.Http, Value = "https://api.example.com" }, TestContext.Current.CancellationToken);
        await scenarios.InsertAsync(new TestScenario
        {
            Id = scenarioId,
            Name = "Get a pet",
            SpecificationId = specificationId,
            MockEndpointId = endpointId,
            ConnectionId = connectionId
        }, TestContext.Current.CancellationToken);

        var handler = TestRunHandlers.Run(scenarios, specifications, connections, new FakeMessageSender(sendResult), new FakeMessageListener(new MessageListenResult(false, "unused")), new SchemaValidator(), callRecords);
        return (handler, scenarioId, callRecords, scenarios);
    }
}
