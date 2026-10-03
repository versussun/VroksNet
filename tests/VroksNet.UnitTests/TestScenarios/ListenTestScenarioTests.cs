using VroksNet.Application.Abstractions;
using VroksNet.Application.TestScenarios.CreateTestScenario;
using VroksNet.Application.TestScenarios.RunTestScenario;
using VroksNet.Domain.ApiSpecifications;
using VroksNet.Domain.CallRecords;
using VroksNet.Domain.Connections;
using VroksNet.Domain.MockEndpoints;
using VroksNet.Domain.TestScenarios;
using VroksNet.Infrastructure.SchemaValidation;
using VroksNet.UnitTests.TestDoubles;

namespace VroksNet.UnitTests.TestScenarios;

/// <summary>Listen-mode Test Scenarios ("Phase C"): defaults, validation on create, and the run's receive-then-validate branch.</summary>
public class ListenTestScenarioTests
{
    private const string OrderSchema = """{ "type": "object", "required": ["orderId"], "properties": { "orderId": { "type": "string" } } }""";

    private readonly FakeApiSpecificationRepository _specifications = new();
    private readonly FakeConnectionRepository _connections = new();
    private readonly FakeTestScenarioRepository _scenarios = new();
    private readonly FakeCallRecordRepository _callRecords = new();
    private readonly Guid _specificationId = Guid.NewGuid();
    private readonly Guid _brokerEndpointId = Guid.NewGuid();
    private readonly Guid _httpEndpointId = Guid.NewGuid();
    private readonly Guid _rabbitConnectionId = Guid.NewGuid();
    private readonly Guid _httpConnectionId = Guid.NewGuid();

    [Theory]
    [InlineData("orders.created:send", TestScenarioKind.Listen)]
    [InlineData("orders.shipped:receive", TestScenarioKind.Send)]
    [InlineData("GET /pets", TestScenarioKind.Send)]
    public void DefaultKindFor_ListensOnlyToOperationsTheServicePublishes(string operationKey, TestScenarioKind expected)
        => Assert.Equal(expected, TestScenarioListening.DefaultKindFor(operationKey));

    [Theory]
    [InlineData("orders.created", "orders.created")]
    [InlineData("orders.{region}.created", "orders.*.created")]
    [InlineData("{tenant}.orders.{id}", "*.orders.*")]
    [InlineData("user/{userId}/signedup", null)]  // parameter inside a "/"-separated segment
    [InlineData("orders.eu-{region}.created", null)] // parameter is only part of a segment
    public void SubscriptionPatternOf_TurnsWholeSegmentParametersIntoWildcards(string channelAddress, string? expected)
        => Assert.Equal(expected, TestScenarioListening.SubscriptionPatternOf(channelAddress));

    [Theory]
    [InlineData("orders.{region}.created:send", true)]
    [InlineData("user/{userId}/signedup:send", false)]
    [InlineData("GET /pets", false)]
    public void CanListen_RequiresASubscribableChannel(string operationKey, bool expected)
        => Assert.Equal(expected, TestScenarioListening.CanListen(operationKey));

    [Fact]
    public async Task Create_UnknownKind_Throws()
    {
        await ArrangeAsync();

        await Assert.ThrowsAsync<ArgumentException>(async () => await CreateHandler.Handle(
            new CreateTestScenario("Bad kind", _specificationId, _brokerEndpointId, _rabbitConnectionId, null, (TestScenarioKind)7),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Create_ListenThroughNats_DropsTheRabbitMqExchange()
    {
        await ArrangeAsync();
        var natsConnectionId = Guid.NewGuid();
        await _connections.InsertAsync(new Connection { Id = natsConnectionId, Name = "NATS", ServiceType = ConnectionServiceType.Nats, Value = "nats://localhost" }, TestContext.Current.CancellationToken);

        var id = await CreateHandler.Handle(
            new CreateTestScenario("Listen via NATS", _specificationId, _brokerEndpointId, natsConnectionId, null, TestScenarioKind.Listen, 5, "stale.exchange"),
            TestContext.Current.CancellationToken);

        var stored = await _scenarios.FindByIdAsync(id, TestContext.Current.CancellationToken);
        Assert.Null(stored!.Exchange);
        Assert.Equal(5, stored.ListenTimeoutSeconds);
    }

    [Fact]
    public async Task Create_ListenOnHttpOperation_Throws()
    {
        await ArrangeAsync();

        await Assert.ThrowsAsync<ArgumentException>(async () => await CreateHandler.Handle(
            new CreateTestScenario("Listen to HTTP", _specificationId, _httpEndpointId, _httpConnectionId, null, TestScenarioKind.Listen),
            TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(TestScenarioListening.MaxTimeoutSeconds + 1)]
    public async Task Create_ListenTimeoutOutOfRange_Throws(int timeoutSeconds)
    {
        await ArrangeAsync();

        await Assert.ThrowsAsync<ArgumentException>(async () => await CreateHandler.Handle(
            new CreateTestScenario("Bad timeout", _specificationId, _brokerEndpointId, _rabbitConnectionId, null, TestScenarioKind.Listen, timeoutSeconds),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Create_SendScenario_DropsTheTimeoutButKeepsTheRabbitMqExchange()
    {
        await ArrangeAsync();

        var id = await CreateHandler.Handle(
            new CreateTestScenario("Publish", _specificationId, _brokerEndpointId, _rabbitConnectionId, null, TestScenarioKind.Send, 5, "my.exchange"),
            TestContext.Current.CancellationToken);

        var stored = await _scenarios.FindByIdAsync(id, TestContext.Current.CancellationToken);
        Assert.Equal(TestScenarioKind.Send, stored!.Kind);
        Assert.Null(stored.ListenTimeoutSeconds);
        Assert.Equal("my.exchange", stored.Exchange);
    }

    [Fact]
    public async Task Create_SendThroughHttp_DropsTheExchange()
    {
        await ArrangeAsync();

        var id = await CreateHandler.Handle(
            new CreateTestScenario("Http", _specificationId, _httpEndpointId, _httpConnectionId, null, TestScenarioKind.Send, Exchange: "my.exchange"),
            TestContext.Current.CancellationToken);

        Assert.Null((await _scenarios.FindByIdAsync(id, TestContext.Current.CancellationToken))!.Exchange);
    }

    [Fact]
    public async Task Run_Send_PublishesToTheScenariosExchange()
    {
        await ArrangeAsync();
        var id = await CreateHandler.Handle(
            new CreateTestScenario("Publish", _specificationId, _brokerEndpointId, _rabbitConnectionId, """{"orderId":"ord_1"}""", TestScenarioKind.Send, Exchange: " amq.topic "),
            TestContext.Current.CancellationToken);
        var sender = new FakeMessageSender(new MessageSendResult(true, "Published."));

        var result = await TestRunHandlers.Run(_scenarios, _specifications, _connections, sender, new FakeMessageListener(new MessageListenResult(false, "unused")), new SchemaValidator(), _callRecords)
            .Handle(new RunTestScenario(id), TestContext.Current.CancellationToken);

        Assert.True(result!.Success);
        Assert.Equal("amq.topic", sender.LastSend!.Value.Exchange);
    }

    [Fact]
    public async Task Run_Listen_ReceivedMessageMatchingSchema_SucceedsAndLogsInboundMessage()
    {
        var listener = new FakeMessageListener(new MessageListenResult(true, "Received.", """{"orderId":"ord_1"}"""));
        var scenarioId = await ArrangeListenScenarioAsync(timeoutSeconds: 5, exchange: "orders");

        var result = await RunHandler(listener).Handle(new RunTestScenario(scenarioId), TestContext.Current.CancellationToken);

        Assert.True(result!.Success);
        Assert.True(result.ContractValidation!.IsValid);
        Assert.Equal("""{"orderId":"ord_1"}""", result.ResponseBody);
        Assert.Equal(TimeSpan.FromSeconds(5), listener.LastListen!.Value.Timeout);
        Assert.Equal("orders", listener.LastListen.Value.Exchange);
        Assert.Equal("orders.created:send", listener.LastListen.Value.OperationKey);

        var logged = Assert.Single(_callRecords.Inserted);
        Assert.Equal(CallDirection.InboundBrokerMessage, logged.Direction);
        Assert.Equal(scenarioId, logged.TestScenarioId);
        Assert.Null(logged.RequestSnapshot);
        Assert.Equal("""{"orderId":"ord_1"}""", logged.ResponseSnapshot);
        Assert.True(logged.ContractValid);
    }

    [Fact]
    public async Task Run_Listen_DefaultsTimeoutAndExchangeWhenUnset()
    {
        var listener = new FakeMessageListener(new MessageListenResult(true, "Received.", """{"orderId":"ord_1"}"""));
        var scenarioId = await ArrangeListenScenarioAsync(timeoutSeconds: null, exchange: null);

        await RunHandler(listener).Handle(new RunTestScenario(scenarioId), TestContext.Current.CancellationToken);

        Assert.Equal(TimeSpan.FromSeconds(TestScenarioListening.DefaultTimeoutSeconds), listener.LastListen!.Value.Timeout);
        Assert.Equal(TestScenarioListening.DefaultRabbitMqExchange, listener.LastListen.Value.Exchange);
    }

    [Fact]
    public async Task Run_Listen_MessageViolatingSchema_FailsWithMessageViolation()
    {
        var listener = new FakeMessageListener(new MessageListenResult(true, "Received.", """{"orderId":42}"""));
        var scenarioId = await ArrangeListenScenarioAsync(timeoutSeconds: 5, exchange: null);

        var result = await RunHandler(listener).Handle(new RunTestScenario(scenarioId), TestContext.Current.CancellationToken);

        Assert.False(result!.Success);
        Assert.Contains("message doesn't match the spec", result.Message);
        Assert.False(result.ContractValidation!.IsValid);
        Assert.False(Assert.Single(_callRecords.Inserted).ContractValid);
    }

    [Fact]
    public async Task Run_Listen_NothingReceived_FailsWithoutValidation()
    {
        var listener = new FakeMessageListener(new MessageListenResult(false, "No message within 5s."));
        var scenarioId = await ArrangeListenScenarioAsync(timeoutSeconds: 5, exchange: null);

        var result = await RunHandler(listener).Handle(new RunTestScenario(scenarioId), TestContext.Current.CancellationToken);

        Assert.False(result!.Success);
        Assert.Equal("No message within 5s.", result.Message);
        Assert.Null(result.ContractValidation);
        var logged = Assert.Single(_callRecords.Inserted);
        Assert.Equal("No message within 5s.", logged.ResponseSnapshot);
        Assert.Null(logged.ContractValid);

        var stored = await _scenarios.FindByIdAsync(scenarioId, TestContext.Current.CancellationToken);
        Assert.False(stored!.LastRunSuccess);
    }

    private CreateTestScenarioHandler CreateHandler => new(_scenarios, _specifications, _connections);

    private RunTestScenarioHandler RunHandler(FakeMessageListener listener) => TestRunHandlers.Run(
        _scenarios, _specifications, _connections,
        new FakeMessageSender(new MessageSendResult(false, "should not be called")),
        listener, new SchemaValidator(), _callRecords);

    private async Task ArrangeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _specifications.UpsertAsync(new ApiSpecification
        {
            Id = _specificationId,
            Title = "Orders",
            Kind = SpecificationKind.AsyncApi,
            Endpoints =
            [
                new MockEndpoint { Id = _brokerEndpointId, SpecificationId = _specificationId, OperationKey = "orders.created:send", ResponseSchema = OrderSchema },
                new MockEndpoint { Id = _httpEndpointId, SpecificationId = _specificationId, OperationKey = "GET /pets" }
            ]
        }, cancellationToken);
        await _connections.InsertAsync(new Connection { Id = _rabbitConnectionId, Name = "Broker", ServiceType = ConnectionServiceType.RabbitMq, Value = "amqp://localhost" }, cancellationToken);
        await _connections.InsertAsync(new Connection { Id = _httpConnectionId, Name = "API", ServiceType = ConnectionServiceType.Http, Value = "https://api.example.com" }, cancellationToken);
    }

    private async Task<Guid> ArrangeListenScenarioAsync(int? timeoutSeconds, string? exchange)
    {
        await ArrangeAsync();
        return await CreateHandler.Handle(
            new CreateTestScenario("Listen for orders", _specificationId, _brokerEndpointId, _rabbitConnectionId, null, TestScenarioKind.Listen, timeoutSeconds, exchange),
            TestContext.Current.CancellationToken);
    }
}
