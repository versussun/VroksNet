using System.Net;
using StackExchange.Redis;
using VroksNet.Application.TestScenarios.CreateTestScenario;
using VroksNet.Domain.ApiSpecifications;
using VroksNet.Domain.Connections;
using VroksNet.Domain.MockEndpoints;
using VroksNet.Domain.TestScenarios;
using VroksNet.Infrastructure.Brokers.Redis;
using VroksNet.Infrastructure.Scheduling;
using VroksNet.UnitTests.TestDoubles;

namespace VroksNet.UnitTests.Brokers;

/// <summary>
/// The Redis adapter (N3) against unreachable targets and invalid input, and its options through
/// create — fast and offline. Talking to a real server is covered in IntegrationTests' Brokers/Redis family.
/// </summary>
public class RedisBrokerAdapterTests
{
    private static readonly RedisBrokerAdapter Adapter = new();

    [Theory]
    [InlineData("localhost:6379,password=secret", "localhost", 6379, null, "secret", false)] // what Aspire's WithReference hands out
    [InlineData("cache.example.com:6380,ssl=true,password=p", "cache.example.com", 6380, null, "p", true)]
    [InlineData("redis://localhost", "localhost", 6379, null, null, false)]
    [InlineData("rediss://user:p%40ss@cache.example.com:6380", "cache.example.com", 6380, "user", "p@ss", true)]
    [InlineData("redis://secret@host", "host", 6379, null, "secret", false)] // a lone user-info part is the password
    [InlineData("redis://:secret@host", "host", 6379, null, "secret", false)]
    public void ConfigurationOf_ReadsBothForms(string value, string host, int port, string? user, string? password, bool ssl)
    {
        var configuration = RedisBrokerAdapter.ConfigurationOf(value, TimeSpan.FromSeconds(1))!;

        var endpoint = Assert.IsType<DnsEndPoint>(Assert.Single(configuration.EndPoints));
        Assert.Equal((host, port, user, password, ssl), (endpoint.Host, endpoint.Port, configuration.User, configuration.Password, configuration.Ssl));
    }

    [Fact]
    public void ConfigurationOf_UriDatabase_AndOwnTimeouts()
    {
        var configuration = RedisBrokerAdapter.ConfigurationOf("redis://host/2", TimeSpan.FromSeconds(3))!;

        Assert.Equal(2, configuration.DefaultDatabase);
        Assert.Equal((3000, 3000, true), (configuration.ConnectTimeout, configuration.AsyncTimeout, configuration.AbortOnConnectFail));
    }

    [Theory]
    [InlineData("")]
    [InlineData("password=secret")]          // no endpoint
    [InlineData("http://host:6379")]
    [InlineData("redis://host/not-a-db")]
    public async Task InvalidValue_FailsWithoutConnecting(string value)
    {
        var result = await Adapter.TestAsync(Connection(value), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.StartsWith("Not a valid Redis connection string", result.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("stream")]
    public async Task UnreachableServer_FailsWithoutThrowing_AndWithoutItsConnectionAdvice(string? mode)
    {
        var options = BrokerOptions.From(mode is null ? null : [new(RedisBrokerAdapter.ModeOption, mode)]);
        var test = await Adapter.TestAsync(Connection("127.0.0.1:1"), TestContext.Current.CancellationToken);
        var send = await Adapter.SendAsync(Connection("127.0.0.1:1"), "orders.created:send", "{}", options, TestContext.Current.CancellationToken);

        Assert.False(test.Success);
        Assert.False(send.Success);
        Assert.StartsWith("Couldn't connect to the server", send.Message);
        Assert.DoesNotContain("abortConnect", send.Message);
    }

    [Fact]
    public void PayloadOf_PrefersThePayloadField_ThenTheOnlyField_ThenAllAsJson()
    {
        Assert.Equal("{\"a\":1}", RedisBrokerAdapter.PayloadOf([new("other", "x"), new("payload", "{\"a\":1}")]));
        Assert.Equal("hello", RedisBrokerAdapter.PayloadOf([new("body", "hello")]));
        Assert.Equal("{\"id\":\"1\",\"kind\":\"created\"}", RedisBrokerAdapter.PayloadOf([new("id", "1"), new("kind", "created")]));
    }

    [Theory]
    [InlineData("orders.{region}.created", null, false)]
    [InlineData("orders.{region}.created", "pubsub", false)]
    [InlineData("orders.{region}.created", "stream", true)] // a stream is one key
    [InlineData("orders.created", "stream", false)]
    public void WhyCantListen_OnlyAStreamWithParameters(string channelAddress, string? mode, bool refused)
    {
        var options = BrokerOptions.From(mode is null ? null : [new(RedisBrokerAdapter.ModeOption, mode)]);

        var reason = Adapter.WhyCantListen(ChannelPattern.Parse(channelAddress)!, options);

        Assert.Equal(refused, reason is not null);
    }

    [Fact]
    public async Task Create_ModeIsMatchedCaseInsensitively_AndStoredAsDeclared()
    {
        var (handler, scenarios, specificationId, endpointId, connectionId) = await ArrangeAsync("orders.created:send");

        var id = await handler.Handle(
            new CreateTestScenario("Stream", specificationId, endpointId, connectionId, null,
                BrokerOptions: new Dictionary<string, string?> { ["Mode"] = "STREAM" }),
            TestContext.Current.CancellationToken);

        Assert.Equal("stream", (await scenarios.FindByIdAsync(id, TestContext.Current.CancellationToken))!.BrokerOptions![RedisBrokerAdapter.ModeOption]);
    }

    [Fact]
    public async Task Create_StreamListenOnAChannelWithParameters_Throws()
    {
        var (handler, _, specificationId, endpointId, connectionId) = await ArrangeAsync("orders.{region}.created:send");

        var error = await Assert.ThrowsAsync<ArgumentException>(async () => await handler.Handle(
            new CreateTestScenario("Stream listen", specificationId, endpointId, connectionId, null, TestScenarioKind.Listen,
                BrokerOptions: new Dictionary<string, string?> { ["mode"] = "stream" }),
            TestContext.Current.CancellationToken));

        Assert.Contains("a Redis stream is one key", error.Message);
    }

    private static async Task<(CreateTestScenarioHandler Handler, FakeTestScenarioRepository Scenarios, Guid SpecificationId, Guid EndpointId, Guid ConnectionId)> ArrangeAsync(string operationKey)
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
            Id = specificationId, Title = "Orders", Kind = SpecificationKind.AsyncApi,
            Endpoints = [new MockEndpoint { Id = endpointId, SpecificationId = specificationId, OperationKey = operationKey }]
        }, cancellationToken);
        await connections.InsertAsync(Connection("localhost:6379", connectionId), cancellationToken);
        return (new CreateTestScenarioHandler(scenarios, specifications, connections, new CronSchedule(), BrokerAdapters.Registry()), scenarios, specificationId, endpointId, connectionId);
    }

    private static Connection Connection(string value, Guid? id = null)
        => new() { Id = id ?? Guid.NewGuid(), Name = "Redis", ServiceType = ConnectionServiceType.Redis, Value = value };
}
