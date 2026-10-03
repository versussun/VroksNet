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

    /// <summary>The UI offers Listen by <see cref="ServiceTypeTraits.CanListen"/>, so it must match what the adapter can do.</summary>
    [Fact]
    public void EveryAdapter_ListensExactlyWhenItsTraitsSayItCan()
    {
        var registry = BrokerAdapters.Registry();

        Assert.All(ServiceTypeTraits.All, traits =>
            Assert.Equal(traits.CanListen, registry.Find(traits.Type) is IListeningBrokerAdapter));
    }

    [Fact]
    public void Constructor_TwoAdaptersForOneType_Throws()
        => Assert.Throws<ArgumentException>(() => new BrokerAdapterRegistry([new NatsBrokerAdapterStub(), new NatsBrokerAdapterStub()]));

    private sealed class NatsBrokerAdapterStub : IBrokerAdapter
    {
        public ConnectionServiceType Type => ConnectionServiceType.Nats;

        public IReadOnlyList<Application.Abstractions.BrokerOptionDefinition> Options => [];

        public Task<Application.Abstractions.ConnectionTestResult> TestAsync(Connection connection, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Application.Abstractions.MessageSendResult> SendAsync(Connection connection, string operationKey, string? payload, BrokerOptions? options, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
