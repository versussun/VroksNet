using VroksNet.Application.TestScenarios.CreateTestScenario;
using VroksNet.Domain.ApiSpecifications;
using VroksNet.Domain.Connections;
using VroksNet.Domain.MockEndpoints;
using VroksNet.Domain.TestScenarios;
using VroksNet.Infrastructure.Brokers.ServiceBus;
using VroksNet.Infrastructure.Scheduling;
using VroksNet.UnitTests.TestDoubles;

namespace VroksNet.UnitTests.Brokers;

/// <summary>
/// The Azure Service Bus adapter (N4) against unreachable targets and invalid input, and its
/// options through create — fast and offline. Talking to a real namespace is covered in
/// IntegrationTests' Brokers/ServiceBus family, against the emulator.
/// </summary>
public class ServiceBusBrokerAdapterTests
{
    private static readonly ServiceBusBrokerAdapter Adapter = new();

    /// <summary>A namespace nothing listens on; the emulator flag allows plain AMQP on any port.</summary>
    private const string Unreachable = "Endpoint=sb://127.0.0.1:1;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=secret-key;UseDevelopmentEmulator=true;";

    [Theory]
    [InlineData("Endpoint=sb://localhost:5672;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;", null)] // what Aspire's WithReference hands out for the emulator
    [InlineData("Endpoint=sb://shop.servicebus.windows.net/;SharedAccessKeyName=send;SharedAccessKey=abc=", null)]
    [InlineData("Endpoint=sb://shop.servicebus.windows.net/;SharedAccessKeyName=send;SharedAccessKey=abc=;EntityPath=orders", "orders")]
    [InlineData("  Endpoint=sb://shop.servicebus.windows.net/;SharedAccessSignature=SharedAccessSignature sr=x&sig=y&se=1&skn=k  ", null)]
    public void ConnectionStringOf_AcceptsSharedAccessConnectionStrings(string value, string? entityPath)
    {
        var connectionString = ServiceBusBrokerAdapter.ConnectionStringOf(value);

        Assert.NotNull(connectionString);
        Assert.Equal(entityPath, connectionString.EntityPath);
    }

    [Theory]
    [InlineData("")]
    [InlineData("shop.servicebus.windows.net")]                                // Microsoft Entra ID: a namespace alone
    [InlineData("Endpoint=sb://shop.servicebus.windows.net/")]                // no credentials
    [InlineData("Endpoint=sb://shop.servicebus.windows.net/;SharedAccessKeyName=send")]
    [InlineData("SharedAccessKeyName=send;SharedAccessKey=abc")]              // no endpoint
    public async Task InvalidValue_FailsWithoutConnecting(string value)
    {
        var result = await Adapter.TestAsync(Connection(value), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.StartsWith("Not a valid Azure Service Bus connection string", result.Message);
    }

    [Fact]
    public async Task UnreachableNamespace_FailsWithoutThrowing_AndWithoutTheKey()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var named = BrokerOptions.From([new(ServiceBusBrokerAdapter.SubscriptionOption, "vroksnet")]);

        var test = await Adapter.TestAsync(Connection(Unreachable), cancellationToken);
        var send = await Adapter.SendAsync(Connection(Unreachable), "orders:send", "{}", null, cancellationToken);
        var listenNamed = await Adapter.ListenAsync(Connection(Unreachable), ChannelPattern.Parse("orders")!, TimeSpan.FromSeconds(1), named, cancellationToken);
        var listenTemporary = await Adapter.ListenAsync(Connection(Unreachable), ChannelPattern.Parse("orders")!, TimeSpan.FromSeconds(1), null, cancellationToken);

        Assert.All([(test.Success, test.Message), (send.Success, send.Message), (listenNamed.Received, listenNamed.Message), (listenTemporary.Received, listenTemporary.Message)], result =>
        {
            Assert.False(result.Item1);
            Assert.False(string.IsNullOrWhiteSpace(result.Item2));
            Assert.DoesNotContain("secret-key", result.Item2);
        });
    }

    [Fact]
    public async Task EntityPathForAnotherEntity_FailsWithoutConnecting()
    {
        var connection = Connection("Endpoint=sb://127.0.0.1:1;SharedAccessKeyName=k;SharedAccessKey=v;UseDevelopmentEmulator=true;EntityPath=payments");

        var send = await Adapter.SendAsync(connection, "orders:send", "{}", null, TestContext.Current.CancellationToken);
        var listen = await Adapter.ListenAsync(connection, ChannelPattern.Parse("orders")!, TimeSpan.FromSeconds(1), null, TestContext.Current.CancellationToken);

        Assert.Contains("limited to \"payments\"", send.Message);
        Assert.Contains("limited to \"payments\"", listen.Message);
    }

    [Theory]
    [InlineData("warehouse.stock.changed", false)]
    [InlineData("warehouse/{site}/changed", true)] // entity names have no wildcards
    public void WhyCantListen_OnlyAChannelWithParameters(string channelAddress, bool refused)
        => Assert.Equal(refused, Adapter.WhyCantListen(ChannelPattern.Parse(channelAddress)!, null) is not null);

    [Fact]
    public void IsNew_ComparesSequenceNumbersWithinTheirPartition()
    {
        // A partitioned entity puts the partition in the top 16 bits, so partition 0's numbers are
        // below every number in partition 3 — even for a message enqueued later.
        static long Sequence(long partition, long number) => (partition << 48) | number;
        var lastWaiting = new Dictionary<long, long>();
        foreach (var waiting in new[] { Sequence(3, 10), Sequence(0, 5), Sequence(3, 12), Sequence(0, 4) })
        {
            ServiceBusBrokerAdapter.RecordWaiting(lastWaiting, waiting);
        }

        Assert.True(ServiceBusBrokerAdapter.IsNew(lastWaiting, Sequence(0, 6)));  // new, though below partition 3's last
        Assert.False(ServiceBusBrokerAdapter.IsNew(lastWaiting, Sequence(0, 5)));
        Assert.False(ServiceBusBrokerAdapter.IsNew(lastWaiting, Sequence(3, 12)));
        Assert.True(ServiceBusBrokerAdapter.IsNew(lastWaiting, Sequence(3, 13)));
        Assert.True(ServiceBusBrokerAdapter.IsNew(lastWaiting, Sequence(7, 1)));  // a partition nothing was waiting in
        Assert.True(ServiceBusBrokerAdapter.IsNew(new Dictionary<long, long>(), 1)); // an empty subscription
    }

    [Fact]
    public void SubscriptionOption_AppliesToListenOnly()
    {
        var option = Assert.Single(Adapter.Options);

        Assert.Equal((ServiceBusBrokerAdapter.SubscriptionOption, null, null), (option.Name, option.SendDescription, option.SendPlaceholder));
        Assert.NotNull(option.ListenDescription);
    }

    [Fact]
    public async Task Create_ListenOnAChannelWithParameters_Throws()
    {
        var (handler, specificationId, endpointId, connectionId) = await ArrangeAsync("warehouse.{site}.changed:send");

        var error = await Assert.ThrowsAsync<ArgumentException>(async () => await handler.Handle(
            new CreateTestScenario("Listen", specificationId, endpointId, connectionId, null, TestScenarioKind.Listen,
                BrokerOptions: new Dictionary<string, string?> { ["subscription"] = "vroksnet" }),
            TestContext.Current.CancellationToken));

        Assert.Contains("has no wildcards", error.Message);
    }

    private static async Task<(CreateTestScenarioHandler Handler, Guid SpecificationId, Guid EndpointId, Guid ConnectionId)> ArrangeAsync(string operationKey)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var specifications = new FakeApiSpecificationRepository();
        var connections = new FakeConnectionRepository();
        var specificationId = Guid.NewGuid();
        var endpointId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        await specifications.UpsertAsync(new ApiSpecification
        {
            Id = specificationId, Title = "Warehouse", Kind = SpecificationKind.AsyncApi,
            Endpoints = [new MockEndpoint { Id = endpointId, SpecificationId = specificationId, OperationKey = operationKey }]
        }, cancellationToken);
        await connections.InsertAsync(Connection(Unreachable, connectionId), cancellationToken);
        return (new CreateTestScenarioHandler(new FakeTestScenarioRepository(), specifications, connections, new CronSchedule(), BrokerAdapters.Registry()), specificationId, endpointId, connectionId);
    }

    private static Connection Connection(string value, Guid? id = null)
        => new() { Id = id ?? Guid.NewGuid(), Name = "Service Bus", ServiceType = ConnectionServiceType.ServiceBus, Value = value };
}
