using VroksNet.Application.Connections.CreateConnection;
using VroksNet.Application.Connections.DeleteConnection;
using VroksNet.Application.Connections.ListConnections;
using VroksNet.Application.Connections.UpdateConnection;
using VroksNet.Domain.Connections;
using VroksNet.UnitTests.TestDoubles;

namespace VroksNet.UnitTests.Connections;

public class ConnectionHandlersTests
{
    [Fact]
    public async Task CreateConnection_Then_ListConnections_ReturnsItOrderedByName()
    {
        var repository = new FakeConnectionRepository();
        var createHandler = new CreateConnectionHandler(repository);

        await createHandler.Handle(new CreateConnection("Zebra Broker", ConnectionServiceType.RabbitMq, "amqp://localhost"), TestContext.Current.CancellationToken);
        var id = await createHandler.Handle(new CreateConnection("Apple API", ConnectionServiceType.Http, "https://api.example.com"), TestContext.Current.CancellationToken);

        var listHandler = new ListConnectionsHandler(repository);
        var result = await listHandler.Handle(new ListConnections(), TestContext.Current.CancellationToken);

        Assert.Equal(["Apple API", "Zebra Broker"], result.Select(c => c.Name));
        Assert.Equal(id, result.Single(c => c.Name == "Apple API").Id);
    }

    [Fact]
    public async Task CreateConnection_BlankName_Throws()
    {
        var handler = new CreateConnectionHandler(new FakeConnectionRepository());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(new CreateConnection("  ", ConnectionServiceType.Http, "https://api.example.com"), TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task UpdateConnection_ExistingId_UpdatesFields()
    {
        var repository = new FakeConnectionRepository();
        var id = await new CreateConnectionHandler(repository).Handle(
            new CreateConnection("Orders API", ConnectionServiceType.Http, "https://old.example.com"), TestContext.Current.CancellationToken);

        var updateHandler = new UpdateConnectionHandler(repository);
        var found = await updateHandler.Handle(
            new UpdateConnection(id, "Orders API (renamed)", ConnectionServiceType.RabbitMq, "amqp://new"), TestContext.Current.CancellationToken);

        Assert.True(found);
        var stored = await repository.FindByIdAsync(id, TestContext.Current.CancellationToken);
        Assert.NotNull(stored);
        Assert.Equal("Orders API (renamed)", stored.Name);
        Assert.Equal(ConnectionServiceType.RabbitMq, stored.ServiceType);
        Assert.Equal("amqp://new", stored.Value);
    }

    [Fact]
    public async Task UpdateConnection_UnknownId_ReturnsFalse()
    {
        var handler = new UpdateConnectionHandler(new FakeConnectionRepository());

        var found = await handler.Handle(new UpdateConnection(Guid.NewGuid(), "Name", ConnectionServiceType.Http, "https://x"), TestContext.Current.CancellationToken);

        Assert.False(found);
    }

    [Fact]
    public async Task DeleteConnection_ExistingId_RemovesIt()
    {
        var repository = new FakeConnectionRepository();
        var id = await new CreateConnectionHandler(repository).Handle(
            new CreateConnection("Orders API", ConnectionServiceType.Http, "https://api.example.com"), TestContext.Current.CancellationToken);

        var found = await new DeleteConnectionHandler(repository).Handle(new DeleteConnection(id), TestContext.Current.CancellationToken);

        Assert.True(found);
        Assert.Empty(await repository.ListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteConnection_UnknownId_ReturnsFalse()
    {
        var handler = new DeleteConnectionHandler(new FakeConnectionRepository());

        var found = await handler.Handle(new DeleteConnection(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.False(found);
    }
}
