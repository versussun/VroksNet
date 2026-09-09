using VroksNet.Application.Abstractions;
using VroksNet.Application.Connections.CreateConnection;
using VroksNet.Application.Connections.TestConnection;
using VroksNet.Domain.Connections;
using VroksNet.UnitTests.TestDoubles;

namespace VroksNet.UnitTests.Connections;

public class TestConnectionHandlerTests
{
    [Fact]
    public async Task Handle_ExistingId_ReturnsTesterResult()
    {
        var repository = new FakeConnectionRepository();
        var id = await new CreateConnectionHandler(repository).Handle(
            new CreateConnection("Orders API", ConnectionServiceType.Http, "https://api.example.com"), TestContext.Current.CancellationToken);

        var tester = new FakeConnectionTester(new ConnectionTestResult(true, "Reached api.example.com — responded 200 OK."));
        var handler = new TestConnectionHandler(repository, tester);

        var result = await handler.Handle(new TestConnection(id), TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal("Reached api.example.com — responded 200 OK.", result.Message);
        Assert.NotNull(tester.LastTested);
        Assert.Equal(id, tester.LastTested.Id);
    }

    [Fact]
    public async Task Handle_UnknownId_ReturnsNullWithoutCallingTester()
    {
        var tester = new FakeConnectionTester(new ConnectionTestResult(true, "should not be reached"));
        var handler = new TestConnectionHandler(new FakeConnectionRepository(), tester);

        var result = await handler.Handle(new TestConnection(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.Null(tester.LastTested);
    }

    [Fact]
    public async Task Handle_FailedTest_PropagatesFailureResult()
    {
        var repository = new FakeConnectionRepository();
        var id = await new CreateConnectionHandler(repository).Handle(
            new CreateConnection("Orders Broker", ConnectionServiceType.RabbitMq, "amqp://guest:guest@nowhere:5672"), TestContext.Current.CancellationToken);

        var handler = new TestConnectionHandler(repository, new FakeConnectionTester(new ConnectionTestResult(false, "Connection refused")));

        var result = await handler.Handle(new TestConnection(id), TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal("Connection refused", result.Message);
    }
}
