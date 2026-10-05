using VroksNet.Application.Abstractions;
using VroksNet.Application.Publishers.CreatePublisher;
using VroksNet.Application.Publishers.ListDuePublishers;
using VroksNet.Application.Publishers.PublishNow;
using VroksNet.Application.Publishers.UpdatePublisher;
using VroksNet.Domain.ApiSpecifications;
using VroksNet.Domain.CallRecords;
using VroksNet.Domain.Connections;
using VroksNet.Domain.MockEndpoints;
using VroksNet.Domain.Publishers;
using VroksNet.Infrastructure.SchemaValidation;
using VroksNet.Infrastructure.Templating;
using VroksNet.UnitTests.TestDoubles;

namespace VroksNet.UnitTests.Publishers;

/// <summary>Publishers (async mocks, Phase 03): the schedule, validation on create/update, and what one publish does.</summary>
public sealed class PublisherTests
{
    private const string OrderSchema = """{ "type": "object", "required": ["orderId"], "properties": { "orderId": { "type": "string" } } }""";

    private readonly FakePublisherRepository _publishers = new();
    private readonly FakeApiSpecificationRepository _specifications = new();
    private readonly FakeConnectionRepository _connections = new();
    private readonly FakeCallRecordRepository _callRecords = new();
    private readonly Guid _specificationId = Guid.NewGuid();
    private readonly Guid _brokerEndpointId = Guid.NewGuid();
    private readonly Guid _httpEndpointId = Guid.NewGuid();
    private readonly Guid _rpcEndpointId = Guid.NewGuid();
    private readonly Guid _rabbitConnectionId = Guid.NewGuid();
    private readonly Guid _natsConnectionId = Guid.NewGuid();
    private readonly Guid _httpConnectionId = Guid.NewGuid();

    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(true, null, true)]     // never published: due right away
    [InlineData(true, -9, false)]      // 9s ago, every 10s
    [InlineData(true, -10, true)]
    [InlineData(true, -60, true)]
    [InlineData(false, null, false)]   // disabled: never due
    public void IsDue_WhenTheIntervalHasPassedSinceTheLastPublish(bool enabled, int? lastPublishedSecondsAgo, bool expected)
    {
        var publisher = new Publisher
        {
            IsEnabled = enabled,
            IntervalSeconds = 10,
            LastPublishedAt = lastPublishedSecondsAgo is { } seconds ? Now.AddSeconds(seconds) : null
        };

        Assert.Equal(expected, PublisherSchedule.IsDue(publisher, Now));
    }

    [Fact]
    public async Task ListDue_ReturnsOnlyEnabledDuePublishers()
    {
        var due = NewPublisher(enabled: true, lastPublishedAt: Now.AddSeconds(-30));
        await _publishers.InsertAsync(due, TestContext.Current.CancellationToken);
        await _publishers.InsertAsync(NewPublisher(enabled: true, lastPublishedAt: Now.AddSeconds(-1)), TestContext.Current.CancellationToken);
        await _publishers.InsertAsync(NewPublisher(enabled: false, lastPublishedAt: null), TestContext.Current.CancellationToken);

        var ids = await new ListDuePublishersHandler(_publishers).Handle(new ListDuePublishers(Now), TestContext.Current.CancellationToken);

        Assert.Equal([due.Id], ids);
    }

    [Fact]
    public async Task Create_RabbitMq_KeepsTheTrimmedExchange()
    {
        await ArrangeAsync();

        var id = await CreateHandler.Handle(
            new CreatePublisher(" Orders ", _specificationId, _brokerEndpointId, _rabbitConnectionId, null, 5, " amq.topic ", Enabled: true),
            TestContext.Current.CancellationToken);

        var stored = await _publishers.FindByIdAsync(id, TestContext.Current.CancellationToken);
        Assert.Equal("Orders", stored!.Name);
        Assert.Equal("amq.topic", stored.BrokerOptions?["exchange"]);
        Assert.Equal(5, stored.IntervalSeconds);
        Assert.True(stored.IsEnabled);
    }

    [Fact]
    public async Task Create_Nats_DropsTheExchange()
    {
        await ArrangeAsync();

        var id = await CreateHandler.Handle(
            new CreatePublisher("Orders", _specificationId, _brokerEndpointId, _natsConnectionId, null, 5, "amq.topic"),
            TestContext.Current.CancellationToken);

        Assert.Null((await _publishers.FindByIdAsync(id, TestContext.Current.CancellationToken))!.BrokerOptions?["exchange"]);
    }

    [Theory]
    [InlineData("", 5, "broker")]       // no name
    [InlineData("Orders", 0, "broker")] // interval below the minimum
    [InlineData("Orders", 86401, "broker")]
    [InlineData("Orders", 5, "http")]   // an HTTP operation isn't publishable
    public async Task Create_InvalidInput_Throws(string name, int intervalSeconds, string target)
    {
        await ArrangeAsync();
        var (endpointId, connectionId) = target == "http" ? (_httpEndpointId, _httpConnectionId) : (_brokerEndpointId, _rabbitConnectionId);

        await Assert.ThrowsAsync<ArgumentException>(async () => await CreateHandler.Handle(
            new CreatePublisher(name, _specificationId, endpointId, connectionId, null, intervalSeconds),
            TestContext.Current.CancellationToken));
        Assert.Empty(await _publishers.ListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Create_GrpcOperation_IsRefused()
    {
        await ArrangeAsync();

        var error = await Assert.ThrowsAsync<ArgumentException>(async () => await CreateHandler.Handle(
            new CreatePublisher("Orders", _specificationId, _rpcEndpointId, _rabbitConnectionId, null, 5),
            TestContext.Current.CancellationToken));
        // Refused by the operation/connection compatibility check before the Publisher's own rule.
        Assert.Contains("RPC /shop.orders.v1.Orders/GetOrder", error.Message);
        Assert.Empty(await _publishers.ListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Update_UnknownPublisher_ReturnsFalse()
    {
        await ArrangeAsync();

        var found = await new UpdatePublisherHandler(_publishers, _specifications, _connections, BrokerAdapters.Registry()).Handle(
            new UpdatePublisher(Guid.NewGuid(), "Orders", _specificationId, _brokerEndpointId, _rabbitConnectionId, null, 5),
            TestContext.Current.CancellationToken);

        Assert.False(found);
    }

    [Fact]
    public async Task PublishNow_RendersTheTemplate_PublishesToTheExchange_AndLogsIt()
    {
        var id = await ArrangePublisherAsync("""{"orderId":"{{uuid}}","at":"{{now}}"}""", exchange: "amq.topic");
        var sender = new FakeMessageSender(new MessageSendResult(true, "Published."));

        var result = await PublishHandler(sender).Handle(new PublishNow(id), TestContext.Current.CancellationToken);

        Assert.True(result!.Success);
        Assert.True(result.ContractValidation!.IsValid);
        Assert.DoesNotContain("{{", result.Payload);
        var (_, operationKey, payload, exchange) = sender.LastSend!.Value;
        Assert.Equal("orders.created:send", operationKey);
        Assert.Equal(result.Payload, payload);
        Assert.Equal("amq.topic", exchange);

        var logged = Assert.Single(_callRecords.Inserted);
        Assert.Equal(CallDirection.OutboundBrokerPublish, logged.Direction);
        Assert.Equal(id, logged.PublisherId);
        Assert.Null(logged.TestScenarioId);
        Assert.Equal(result.Payload, logged.RequestSnapshot);
        Assert.True(logged.ContractValid);

        var stored = await _publishers.FindByIdAsync(id, TestContext.Current.CancellationToken);
        Assert.True(stored!.LastPublishSuccess);
        Assert.NotNull(stored.LastPublishedAt);
    }

    [Fact]
    public async Task PublishNow_PayloadNotMatchingTheSchema_IsSentButReported()
    {
        var id = await ArrangePublisherAsync("""{"orderId":42}""");

        var result = await PublishHandler(new FakeMessageSender(new MessageSendResult(true, "Published."))).Handle(new PublishNow(id), TestContext.Current.CancellationToken);

        Assert.True(result!.Success);
        Assert.False(result.ContractValidation!.IsValid);
        Assert.Contains("doesn't match the spec", result.Message);
        Assert.False(Assert.Single(_callRecords.Inserted).ContractValid);
    }

    [Fact]
    public async Task PublishNow_RequestPlaceholder_WarnsInTheHistory()
    {
        var id = await ArrangePublisherAsync("""{"orderId":"{{request.path.id}}"}""");

        await PublishHandler(new FakeMessageSender(new MessageSendResult(true, "Published."))).Handle(new PublishNow(id), TestContext.Current.CancellationToken);

        Assert.Contains("request.path.id", Assert.Single(_callRecords.Inserted).Warnings);
    }

    [Fact]
    public async Task PublishNow_SendFails_RecordsTheFailure()
    {
        var id = await ArrangePublisherAsync("""{"orderId":"o-1"}""");

        var result = await PublishHandler(new FakeMessageSender(new MessageSendResult(false, "Timed out after 10s."))).Handle(new PublishNow(id), TestContext.Current.CancellationToken);

        Assert.False(result!.Success);
        Assert.Null(result.ContractValidation);
        Assert.Equal("Timed out after 10s.", Assert.Single(_callRecords.Inserted).ResponseSnapshot);
        Assert.False((await _publishers.FindByIdAsync(id, TestContext.Current.CancellationToken))!.LastPublishSuccess);
    }

    [Fact]
    public async Task PublishNow_DeletedConnection_FailsWithoutSending()
    {
        var id = await ArrangePublisherAsync("""{"orderId":"o-1"}""");
        await _connections.DeleteAsync(_rabbitConnectionId, TestContext.Current.CancellationToken);
        var sender = new FakeMessageSender(new MessageSendResult(true, "should not be called"));

        var result = await PublishHandler(sender).Handle(new PublishNow(id), TestContext.Current.CancellationToken);

        Assert.False(result!.Success);
        Assert.Null(sender.LastSend);
        Assert.False((await _publishers.FindByIdAsync(id, TestContext.Current.CancellationToken))!.LastPublishSuccess);
    }

    [Fact]
    public async Task PublishNow_UnknownPublisher_ReturnsNull()
    {
        var result = await PublishHandler(new FakeMessageSender(new MessageSendResult(true, "unused"))).Handle(new PublishNow(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    private CreatePublisherHandler CreateHandler => new(_publishers, _specifications, _connections, BrokerAdapters.Registry());

    private PublishNowHandler PublishHandler(FakeMessageSender sender) => new(
        _publishers, _specifications, _connections, sender, new ResponseTemplateEngine(TimeProvider.System), new SchemaValidator(), _callRecords);

    private Publisher NewPublisher(bool enabled, DateTimeOffset? lastPublishedAt) => new()
    {
        Id = Guid.NewGuid(),
        Name = "P",
        IntervalSeconds = 10,
        IsEnabled = enabled,
        LastPublishedAt = lastPublishedAt
    };

    private async Task<Guid> ArrangePublisherAsync(string payload, string? exchange = null)
    {
        await ArrangeAsync();
        return await CreateHandler.Handle(
            new CreatePublisher("Orders", _specificationId, _brokerEndpointId, _rabbitConnectionId, payload, 5, exchange),
            TestContext.Current.CancellationToken);
    }

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
                new MockEndpoint { Id = _httpEndpointId, SpecificationId = _specificationId, OperationKey = "GET /pets" },
                new MockEndpoint { Id = _rpcEndpointId, SpecificationId = _specificationId, OperationKey = "RPC /shop.orders.v1.Orders/GetOrder" }
            ]
        }, cancellationToken);
        await _connections.InsertAsync(new Connection { Id = _rabbitConnectionId, Name = "Rabbit", ServiceType = ConnectionServiceType.RabbitMq, Value = "amqp://localhost" }, cancellationToken);
        await _connections.InsertAsync(new Connection { Id = _natsConnectionId, Name = "Nats", ServiceType = ConnectionServiceType.Nats, Value = "nats://localhost" }, cancellationToken);
        await _connections.InsertAsync(new Connection { Id = _httpConnectionId, Name = "API", ServiceType = ConnectionServiceType.Http, Value = "https://api.example.com" }, cancellationToken);
    }
}
