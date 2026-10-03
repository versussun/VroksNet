using VroksNet.UnitTests.TestDoubles;
using VroksNet.Application.System.ListConnectionTypes;
using VroksNet.Domain.Connections;

namespace VroksNet.UnitTests.Connections;

/// <summary><see cref="ServiceTypeTraits"/> describes every type once, and the API serves exactly that (ADR 0003).</summary>
public class ServiceTypeTraitsTests
{
    [Fact]
    public void All_DescribesEveryServiceTypeExactlyOnce()
        => Assert.Equal(Enum.GetValues<ConnectionServiceType>().Order(), ServiceTypeTraits.All.Select(traits => traits.Type).Order());

    [Fact]
    public void All_ListenNoteIsGivenExactlyWhenTheTypeCantListen()
        => Assert.All(ServiceTypeTraits.All, traits => Assert.Equal(traits.CanListen, traits.ListenNote is null));

    [Theory]
    [InlineData(new[] { "KAFKA-SECURE" }, new[] { ConnectionServiceType.Kafka })]
    [InlineData(new[] { "nats", "amqp" }, new[] { ConnectionServiceType.RabbitMq, ConnectionServiceType.Nats })]
    [InlineData(new[] { "secure-mqtt" }, new[] { ConnectionServiceType.Mqtt })]
    [InlineData(new[] { "Redis" }, new[] { ConnectionServiceType.Redis })]
    [InlineData(new[] { "stomp" }, new ConnectionServiceType[0])]
    [InlineData(new[] { "http" }, new ConnectionServiceType[0])] // an AsyncAPI operation can't go through an HTTP connection
    public void TypesFor_MatchesProtocolsCaseInsensitively(string[] protocols, ConnectionServiceType[] expected)
        => Assert.Equal(expected, ServiceTypeTraits.TypesFor(protocols));

    [Fact]
    public void Find_UnknownValue_IsNull()
        => Assert.Null(ServiceTypeTraits.Find((ConnectionServiceType)99));

    [Fact]
    public async Task ListConnectionTypes_ReturnsEveryTypesTraitsInOrder()
    {
        var types = await new ListConnectionTypesHandler(BrokerAdapters.Registry()).Handle(new ListConnectionTypes(), TestContext.Current.CancellationToken);

        Assert.Equal(ServiceTypeTraits.All.Select(traits => traits.Type), types.Select(type => type.Type));
        var http = Assert.Single(types, type => type.Type == ConnectionServiceType.Http);
        Assert.Equal(("HTTP", "URL", true, false), (http.DisplayName, http.ValueLabel, http.IsHttp, http.CanListen));
        Assert.NotNull(http.ListenNote);
        Assert.Empty(http.Options);
        var exchange = Assert.Single(Assert.Single(types, type => type.Type == ConnectionServiceType.RabbitMq).Options);
        Assert.Equal(("exchange", "Exchange", "amq.topic"), (exchange.Name, exchange.Label, exchange.SuggestedValue));
    }
}
