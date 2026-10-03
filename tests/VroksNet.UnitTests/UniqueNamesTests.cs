using VroksNet.Application.Connections.CreateConnection;
using VroksNet.Application.Connections.UpdateConnection;
using VroksNet.Application.Publishers.CreatePublisher;
using VroksNet.Application.Publishers.UpdatePublisher;
using VroksNet.Application.TestScenarios.CreateTestScenario;
using VroksNet.Application.TestScenarios.UpdateTestScenario;
using VroksNet.Domain.ApiSpecifications;
using VroksNet.Domain.Connections;
using VroksNet.Domain.MockEndpoints;
using VroksNet.Domain.TestScenarios;
using VroksNet.UnitTests.TestDoubles;

namespace VroksNet.UnitTests;

/// <summary>
/// Test scenarios, Publishers and connections have unique, trimmed names (step A2): a taken name is
/// refused on create and on rename, while saving an object under its own name is fine.
/// </summary>
public sealed class UniqueNamesTests
{
    private readonly FakeApiSpecificationRepository _specifications = new();
    private readonly FakeConnectionRepository _connections = new();
    private readonly FakeTestScenarioRepository _scenarios = new();
    private readonly FakePublisherRepository _publishers = new();
    private readonly Guid _specificationId = Guid.NewGuid();
    private readonly Guid _httpEndpointId = Guid.NewGuid();
    private readonly Guid _channelEndpointId = Guid.NewGuid();
    private readonly Guid _httpConnectionId = Guid.NewGuid();
    private readonly Guid _natsConnectionId = Guid.NewGuid();

    [Fact]
    public async Task TestScenario_TakenName_IsRefusedOnCreateAndRename_ButKeepingItsOwnNameIsFine()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ArrangeAsync();
        var create = new CreateTestScenarioHandler(_scenarios, _specifications, _connections);
        var update = new UpdateTestScenarioHandler(_scenarios, _specifications, _connections);

        var first = await create.Handle(new CreateTestScenario("  Get pets  ", _specificationId, _httpEndpointId, _httpConnectionId, null), cancellationToken);
        var second = await create.Handle(new CreateTestScenario("Get pets again", _specificationId, _httpEndpointId, _httpConnectionId, null), cancellationToken);
        Assert.Equal("Get pets", (await _scenarios.FindByIdAsync(first, cancellationToken))!.Name);

        var duplicate = await Assert.ThrowsAsync<ArgumentException>(() => create.Handle(new CreateTestScenario("Get pets", _specificationId, _httpEndpointId, _httpConnectionId, null), cancellationToken).AsTask());
        Assert.Equal("A test scenario named \"Get pets\" already exists.", duplicate.Message);
        await Assert.ThrowsAsync<ArgumentException>(() => update.Handle(new UpdateTestScenario(second, " Get pets", _specificationId, _httpEndpointId, _httpConnectionId, null), cancellationToken).AsTask());

        Assert.True(await update.Handle(new UpdateTestScenario(first, "Get pets", _specificationId, _httpEndpointId, _httpConnectionId, "{}"), cancellationToken));
        Assert.Equal("{}", (await _scenarios.FindByIdAsync(first, cancellationToken))!.PayloadOverride);
    }

    [Fact]
    public async Task Publisher_TakenName_IsRefusedOnCreateAndRename()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ArrangeAsync();
        var create = new CreatePublisherHandler(_publishers, _specifications, _connections);
        var update = new UpdatePublisherHandler(_publishers, _specifications, _connections);

        var first = await create.Handle(new CreatePublisher("Orders", _specificationId, _channelEndpointId, _natsConnectionId, null, 5), cancellationToken);
        var second = await create.Handle(new CreatePublisher("Orders fast", _specificationId, _channelEndpointId, _natsConnectionId, null, 1), cancellationToken);

        await Assert.ThrowsAsync<ArgumentException>(() => create.Handle(new CreatePublisher(" Orders ", _specificationId, _channelEndpointId, _natsConnectionId, null, 5), cancellationToken).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => update.Handle(new UpdatePublisher(second, "Orders", _specificationId, _channelEndpointId, _natsConnectionId, null, 1), cancellationToken).AsTask());
        Assert.True(await update.Handle(new UpdatePublisher(first, "Orders", _specificationId, _channelEndpointId, _natsConnectionId, null, 10), cancellationToken));
    }

    [Fact]
    public async Task Connection_TakenName_IsRefusedOnCreateAndRename()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var create = new CreateConnectionHandler(_connections);
        var update = new UpdateConnectionHandler(_connections);

        var first = await create.Handle(new CreateConnection("Orders API ", ConnectionServiceType.Http, "https://a.example"), cancellationToken);
        var second = await create.Handle(new CreateConnection("Payments API", ConnectionServiceType.Http, "https://b.example"), cancellationToken);
        Assert.Equal("Orders API", (await _connections.FindByIdAsync(first, cancellationToken))!.Name);

        var duplicate = await Assert.ThrowsAsync<ArgumentException>(() => create.Handle(new CreateConnection("Orders API", ConnectionServiceType.Http, "https://c.example"), cancellationToken).AsTask());
        Assert.Equal("A connection named \"Orders API\" already exists.", duplicate.Message);
        await Assert.ThrowsAsync<ArgumentException>(() => update.Handle(new UpdateConnection(second, "Orders API", ConnectionServiceType.Http, "https://b.example"), cancellationToken).AsTask());
        Assert.True(await update.Handle(new UpdateConnection(first, "Orders API", ConnectionServiceType.Http, "https://a2.example"), cancellationToken));
    }

    private async Task ArrangeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _specifications.UpsertAsync(new ApiSpecification
        {
            Id = _specificationId,
            Title = "Shop",
            Kind = SpecificationKind.OpenApi,
            Endpoints =
            [
                new MockEndpoint { Id = _httpEndpointId, SpecificationId = _specificationId, OperationKey = "GET /pets" },
                new MockEndpoint { Id = _channelEndpointId, SpecificationId = _specificationId, OperationKey = "orders.created:send" }
            ]
        }, cancellationToken);
        await _connections.InsertAsync(new Connection { Id = _httpConnectionId, Name = "HTTP", ServiceType = ConnectionServiceType.Http, Value = "https://api.example.com" }, cancellationToken);
        await _connections.InsertAsync(new Connection { Id = _natsConnectionId, Name = "NATS", ServiceType = ConnectionServiceType.Nats, Value = "nats://localhost:4222" }, cancellationToken);
    }
}
