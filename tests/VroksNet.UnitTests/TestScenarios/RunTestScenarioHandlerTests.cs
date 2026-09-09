using VroksNet.Application.Abstractions;
using VroksNet.Application.TestScenarios.RunTestScenario;
using VroksNet.Domain.ApiSpecifications;
using VroksNet.Domain.CallRecords;
using VroksNet.Domain.Connections;
using VroksNet.Domain.MockEndpoints;
using VroksNet.Domain.TestScenarios;
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

        var handler = new RunTestScenarioHandler(scenarios, specifications, connections, sender, callRecords);
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

        var handler = new RunTestScenarioHandler(scenarios, specifications, connections, sender, new FakeCallRecordRepository());
        await handler.Handle(new RunTestScenario(scenarioId), TestContext.Current.CancellationToken);

        Assert.Equal("{\"orderId\":\"custom\"}", sender.LastSend!.Value.Payload);
    }

    [Fact]
    public async Task Handle_UnknownScenario_ReturnsNull()
    {
        var handler = new RunTestScenarioHandler(
            new FakeTestScenarioRepository(), new FakeApiSpecificationRepository(), new FakeConnectionRepository(),
            new FakeMessageSender(new MessageSendResult(true, "unused")), new FakeCallRecordRepository());

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

        var handler = new RunTestScenarioHandler(scenarios, specifications, connections, sender, callRecords);
        var result = await handler.Handle(new RunTestScenario(scenarioId), TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Null(sender.LastSend);
        Assert.Empty(callRecords.Inserted);
    }
}
