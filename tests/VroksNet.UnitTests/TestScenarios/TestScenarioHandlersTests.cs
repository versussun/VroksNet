using VroksNet.Application.Connections.CreateConnection;
using VroksNet.Application.TestScenarios.CreateTestScenario;
using VroksNet.Application.TestScenarios.DeleteTestScenario;
using VroksNet.Application.TestScenarios.ListTestScenarios;
using VroksNet.Application.TestScenarios.UpdateTestScenario;
using VroksNet.Domain.ApiSpecifications;
using VroksNet.Domain.Connections;
using VroksNet.Domain.MockEndpoints;
using VroksNet.UnitTests.TestDoubles;

namespace VroksNet.UnitTests.TestScenarios;

public class TestScenarioHandlersTests
{
    [Fact]
    public async Task Create_HttpOperationAndHttpConnection_Succeeds()
    {
        var (specifications, connections, httpEndpointId, _, httpConnectionId, _) = await SeedAsync();
        var handler = new CreateTestScenarioHandler(new FakeTestScenarioRepository(), specifications, connections);

        var id = await handler.Handle(
            new CreateTestScenario("Send GET /pets", SpecId, httpEndpointId, httpConnectionId, null),
            TestContext.Current.CancellationToken);

        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public async Task Create_HttpOperationThroughRabbitMqConnection_Throws()
    {
        var (specifications, connections, httpEndpointId, _, _, rabbitConnectionId) = await SeedAsync();
        var handler = new CreateTestScenarioHandler(new FakeTestScenarioRepository(), specifications, connections);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(new CreateTestScenario("Mismatched", SpecId, httpEndpointId, rabbitConnectionId, null), TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task Create_AsyncApiOperationAndRabbitMqConnection_Succeeds()
    {
        var (specifications, connections, _, asyncEndpointId, _, rabbitConnectionId) = await SeedAsync();
        var handler = new CreateTestScenarioHandler(new FakeTestScenarioRepository(), specifications, connections);

        var id = await handler.Handle(
            new CreateTestScenario("Publish order.created", SpecId, asyncEndpointId, rabbitConnectionId, null),
            TestContext.Current.CancellationToken);

        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public async Task Create_UnknownSpecification_Throws()
    {
        var (specifications, connections, _, _, httpConnectionId, _) = await SeedAsync();
        var handler = new CreateTestScenarioHandler(new FakeTestScenarioRepository(), specifications, connections);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(new CreateTestScenario("x", Guid.NewGuid(), Guid.NewGuid(), httpConnectionId, null), TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task Create_BlankName_Throws()
    {
        var (specifications, connections, httpEndpointId, _, httpConnectionId, _) = await SeedAsync();
        var handler = new CreateTestScenarioHandler(new FakeTestScenarioRepository(), specifications, connections);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(new CreateTestScenario("  ", SpecId, httpEndpointId, httpConnectionId, null), TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task Update_ExistingId_UpdatesFields()
    {
        var (specifications, connections, httpEndpointId, asyncEndpointId, httpConnectionId, rabbitConnectionId) = await SeedAsync();
        var repository = new FakeTestScenarioRepository();
        var id = await new CreateTestScenarioHandler(repository, specifications, connections).Handle(
            new CreateTestScenario("Original", SpecId, httpEndpointId, httpConnectionId, null), TestContext.Current.CancellationToken);

        var updateHandler = new UpdateTestScenarioHandler(repository, specifications, connections);
        var found = await updateHandler.Handle(
            new UpdateTestScenario(id, "Renamed", SpecId, asyncEndpointId, rabbitConnectionId, "{\"custom\":true}"),
            TestContext.Current.CancellationToken);

        Assert.True(found);
        var stored = await repository.FindByIdAsync(id, TestContext.Current.CancellationToken);
        Assert.NotNull(stored);
        Assert.Equal("Renamed", stored.Name);
        Assert.Equal(asyncEndpointId, stored.MockEndpointId);
        Assert.Equal(rabbitConnectionId, stored.ConnectionId);
        Assert.Equal("{\"custom\":true}", stored.PayloadOverride);
    }

    [Fact]
    public async Task Update_UnknownId_ReturnsFalse()
    {
        var (specifications, connections, httpEndpointId, _, httpConnectionId, _) = await SeedAsync();
        var handler = new UpdateTestScenarioHandler(new FakeTestScenarioRepository(), specifications, connections);

        var found = await handler.Handle(
            new UpdateTestScenario(Guid.NewGuid(), "x", SpecId, httpEndpointId, httpConnectionId, null), TestContext.Current.CancellationToken);

        Assert.False(found);
    }

    [Fact]
    public async Task Delete_ExistingId_RemovesIt()
    {
        var (specifications, connections, httpEndpointId, _, httpConnectionId, _) = await SeedAsync();
        var repository = new FakeTestScenarioRepository();
        var id = await new CreateTestScenarioHandler(repository, specifications, connections).Handle(
            new CreateTestScenario("Original", SpecId, httpEndpointId, httpConnectionId, null), TestContext.Current.CancellationToken);

        var found = await new DeleteTestScenarioHandler(repository).Handle(new DeleteTestScenario(id), TestContext.Current.CancellationToken);

        Assert.True(found);
        Assert.Empty(await repository.ListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task List_ReturnsDenormalizedSummaries()
    {
        var (specifications, connections, httpEndpointId, _, httpConnectionId, _) = await SeedAsync();
        var repository = new FakeTestScenarioRepository();
        await new CreateTestScenarioHandler(repository, specifications, connections).Handle(
            new CreateTestScenario("Send GET /pets", SpecId, httpEndpointId, httpConnectionId, null), TestContext.Current.CancellationToken);

        var result = await new ListTestScenariosHandler(repository, specifications, connections)
            .Handle(new ListTestScenarios(), TestContext.Current.CancellationToken);

        var summary = Assert.Single(result);
        Assert.Equal("Send GET /pets", summary.Name);
        Assert.Equal("Petstore", summary.SpecificationTitle);
        Assert.Equal("GET /pets", summary.OperationKey);
        Assert.Equal("Orders API", summary.ConnectionName);
        Assert.Equal(ConnectionServiceType.Http, summary.ConnectionServiceType);
    }

    [Fact]
    public async Task List_DeletedConnection_ShowsPlaceholder()
    {
        var (specifications, connections, httpEndpointId, _, httpConnectionId, _) = await SeedAsync();
        var repository = new FakeTestScenarioRepository();
        await new CreateTestScenarioHandler(repository, specifications, connections).Handle(
            new CreateTestScenario("Send GET /pets", SpecId, httpEndpointId, httpConnectionId, null), TestContext.Current.CancellationToken);

        await connections.DeleteAsync(httpConnectionId, TestContext.Current.CancellationToken);

        var result = await new ListTestScenariosHandler(repository, specifications, connections)
            .Handle(new ListTestScenarios(), TestContext.Current.CancellationToken);

        var summary = Assert.Single(result);
        Assert.Equal("(deleted connection)", summary.ConnectionName);
    }

    private static readonly Guid SpecId = Guid.NewGuid();

    private static async Task<(FakeApiSpecificationRepository Specifications, FakeConnectionRepository Connections, Guid HttpEndpointId, Guid AsyncEndpointId, Guid HttpConnectionId, Guid RabbitConnectionId)> SeedAsync()
    {
        var httpEndpointId = Guid.NewGuid();
        var asyncEndpointId = Guid.NewGuid();

        var specifications = new FakeApiSpecificationRepository();
        await specifications.UpsertAsync(new ApiSpecification
        {
            Id = SpecId,
            Title = "Petstore",
            Kind = SpecificationKind.OpenApi,
            Endpoints =
            [
                new MockEndpoint { Id = httpEndpointId, SpecificationId = SpecId, OperationKey = "GET /pets", ExampleTemplate = "[]" },
                new MockEndpoint { Id = asyncEndpointId, SpecificationId = SpecId, OperationKey = "orders.created:send", ExampleTemplate = "{}" }
            ]
        }, TestContext.Current.CancellationToken);

        var connections = new FakeConnectionRepository();
        var httpConnectionId = Guid.NewGuid();
        var rabbitConnectionId = Guid.NewGuid();
        await connections.InsertAsync(new Connection { Id = httpConnectionId, Name = "Orders API", ServiceType = ConnectionServiceType.Http, Value = "https://api.example.com" }, TestContext.Current.CancellationToken);
        await connections.InsertAsync(new Connection { Id = rabbitConnectionId, Name = "Orders Broker", ServiceType = ConnectionServiceType.RabbitMq, Value = "amqp://localhost" }, TestContext.Current.CancellationToken);

        return (specifications, connections, httpEndpointId, asyncEndpointId, httpConnectionId, rabbitConnectionId);
    }
}
