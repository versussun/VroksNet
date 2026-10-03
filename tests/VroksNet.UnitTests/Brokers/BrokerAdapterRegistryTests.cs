using Microsoft.Extensions.DependencyInjection;
using VroksNet.Domain.Connections;
using VroksNet.Infrastructure;
using VroksNet.Infrastructure.Brokers;
using VroksNet.UnitTests.TestDoubles;

namespace VroksNet.UnitTests.Brokers;

/// <summary>The adapters the app registers cover every <see cref="ConnectionServiceType"/>, once each (ADR 0003).</summary>
public class BrokerAdapterRegistryTests
{
    [Fact]
    public void AddBrokerAdapters_RegistersExactlyOneAdapterPerServiceType()
    {
        var adapters = new ServiceCollection().AddBrokerAdapters().BuildServiceProvider().GetServices<IBrokerAdapter>().ToList();

        Assert.Equal(
            Enum.GetValues<ConnectionServiceType>().Order(),
            adapters.Select(adapter => adapter.Type).Order());
    }

    [Theory]
    [InlineData(ConnectionServiceType.RabbitMq)]
    [InlineData(ConnectionServiceType.Nats)]
    [InlineData(ConnectionServiceType.Kafka)]
    public void Find_Broker_IsAListeningAdapter(ConnectionServiceType type)
        => Assert.IsAssignableFrom<IListeningBrokerAdapter>(BrokerAdapters.Registry().Find(type));

    [Fact]
    public void Find_Http_CantListen()
    {
        var adapter = BrokerAdapters.Registry().Find(ConnectionServiceType.Http);

        Assert.NotNull(adapter);
        Assert.IsNotAssignableFrom<IListeningBrokerAdapter>(adapter);
    }

    [Fact]
    public void Constructor_TwoAdaptersForOneType_Throws()
        => Assert.Throws<ArgumentException>(() => new BrokerAdapterRegistry([new NatsBrokerAdapterStub(), new NatsBrokerAdapterStub()]));

    private sealed class NatsBrokerAdapterStub : IBrokerAdapter
    {
        public ConnectionServiceType Type => ConnectionServiceType.Nats;

        public Task<Application.Abstractions.ConnectionTestResult> TestAsync(Connection connection, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Application.Abstractions.MessageSendResult> SendAsync(Connection connection, string operationKey, string? payload, string? exchange, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
