using VroksNet.Application.TestScenarios.CreateTestScenario;
using VroksNet.Domain.ApiSpecifications;
using VroksNet.Domain.Connections;
using VroksNet.Domain.MockEndpoints;
using VroksNet.Infrastructure.Brokers.Mqtt;
using VroksNet.Infrastructure.Scheduling;
using VroksNet.UnitTests.TestDoubles;

namespace VroksNet.UnitTests.Brokers;

/// <summary>
/// The MQTT adapter (N2) against unreachable targets and invalid input, and its options through
/// create — fast and offline. Talking to a real broker is covered in IntegrationTests' Brokers/Mqtt family.
/// </summary>
public class MqttBrokerAdapterTests
{
    private static readonly MqttBrokerAdapter Adapter = new();

    [Theory]
    [InlineData("mqtt://localhost", "localhost", 1883)]
    [InlineData("mqtts://broker.example.com", "broker.example.com", 8883)]
    [InlineData("mqtt://user:p%40ss@host:2883", "host", 2883)]
    public void ClientOptionsOf_ReadsHostPortAndDefaults(string value, string host, int port)
    {
        var options = MqttBrokerAdapter.ClientOptionsOf(value, TimeSpan.FromSeconds(1));

        var tcp = Assert.IsType<MQTTnet.MqttClientTcpOptions>(options!.ChannelOptions);
        var endpoint = Assert.IsType<System.Net.DnsEndPoint>(tcp.RemoteEndpoint);
        Assert.Equal((host, port), (endpoint.Host, endpoint.Port));
    }

    [Fact]
    public void ClientOptionsOf_Credentials_AreUnescaped()
    {
        var options = MqttBrokerAdapter.ClientOptionsOf("mqtt://user:p%40ss@host", TimeSpan.FromSeconds(1))!;

        Assert.Equal("user", options.Credentials!.GetUserName(options));
        Assert.Equal("p@ss", System.Text.Encoding.UTF8.GetString(options.Credentials.GetPassword(options)));
    }

    [Theory]
    [InlineData("host:1883")]
    [InlineData("tcp://host:1883")]
    [InlineData("not a uri")]
    public async Task InvalidValue_FailsWithoutConnecting(string value)
    {
        var result = await Adapter.TestAsync(Connection(value), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.StartsWith("Not a valid MQTT connection string", result.Message);
    }

    [Fact]
    public async Task UnreachableBroker_FailsWithoutThrowing()
    {
        var test = await Adapter.TestAsync(Connection("mqtt://127.0.0.1:1"), TestContext.Current.CancellationToken);
        var send = await Adapter.SendAsync(Connection("mqtt://127.0.0.1:1"), "orders/created:send", "{}", null, TestContext.Current.CancellationToken);

        Assert.False(test.Success);
        Assert.False(send.Success);
        Assert.False(string.IsNullOrWhiteSpace(send.Message));
    }

    [Fact]
    public async Task Create_MqttOptions_AreMatchedCaseInsensitively_AndStoredAsDeclared()
    {
        var (handler, scenarios, specificationId, endpointId, connectionId) = await ArrangeAsync();

        var id = await handler.Handle(
            new CreateTestScenario("Options", specificationId, endpointId, connectionId, null,
                BrokerOptions: new Dictionary<string, string?> { ["QoS"] = "1", ["retain"] = "TRUE" }),
            TestContext.Current.CancellationToken);

        var stored = (await scenarios.FindByIdAsync(id, TestContext.Current.CancellationToken))!.BrokerOptions!;
        Assert.Equal(("1", "true"), (stored["qos"], stored["retain"]));
    }

    [Fact]
    public async Task Create_QosOutOfRange_Throws()
    {
        var (handler, _, specificationId, endpointId, connectionId) = await ArrangeAsync();

        var error = await Assert.ThrowsAsync<ArgumentException>(async () => await handler.Handle(
            new CreateTestScenario("Bad qos", specificationId, endpointId, connectionId, null, BrokerOptions: new Dictionary<string, string?> { ["qos"] = "3" }),
            TestContext.Current.CancellationToken));

        Assert.Contains("takes \"0\", \"1\", \"2\"", error.Message);
    }

    private static async Task<(CreateTestScenarioHandler Handler, FakeTestScenarioRepository Scenarios, Guid SpecificationId, Guid EndpointId, Guid ConnectionId)> ArrangeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var specifications = new FakeApiSpecificationRepository();
        var connections = new FakeConnectionRepository();
        var scenarios = new FakeTestScenarioRepository();
        var specificationId = Guid.NewGuid();
        var endpointId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        await specifications.UpsertAsync(new ApiSpecification
        {
            Id = specificationId, Title = "Devices", Kind = SpecificationKind.AsyncApi,
            Endpoints = [new MockEndpoint { Id = endpointId, SpecificationId = specificationId, OperationKey = "devices/{id}/telemetry:send" }]
        }, cancellationToken);
        await connections.InsertAsync(Connection("mqtt://localhost", connectionId), cancellationToken);
        return (new CreateTestScenarioHandler(scenarios, specifications, connections, new CronSchedule(), BrokerAdapters.Registry()), scenarios, specificationId, endpointId, connectionId);
    }

    private static Connection Connection(string value, Guid? id = null)
        => new() { Id = id ?? Guid.NewGuid(), Name = "MQTT", ServiceType = ConnectionServiceType.Mqtt, Value = value };
}
