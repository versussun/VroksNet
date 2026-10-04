using VroksNet.Application.TestScenarios.CreateTestScenario;
using VroksNet.Domain.ApiSpecifications;
using VroksNet.Domain.Connections;
using VroksNet.Domain.MockEndpoints;
using VroksNet.Domain.TestScenarios;
using VroksNet.Infrastructure.Brokers.Aws;
using VroksNet.Infrastructure.Scheduling;
using VroksNet.UnitTests.TestDoubles;

namespace VroksNet.UnitTests.Brokers;

/// <summary>
/// The AWS SQS and SNS adapters (N5) against unreachable targets and invalid input, and their
/// options through create — fast and offline. Talking to real services is covered in
/// IntegrationTests' Brokers/Aws family, against LocalStack.
/// </summary>
public class AwsBrokerAdapterTests
{
    private static readonly SqsBrokerAdapter Sqs = new();
    private static readonly SnsBrokerAdapter Sns = new();

    /// <summary>An endpoint nothing listens on.</summary>
    private const string Unreachable = "Region=us-east-1;AccessKeyId=AKIAEXAMPLE;SecretAccessKey=secret-key;ServiceUrl=http://127.0.0.1:1";

    [Theory]
    [InlineData("Region=eu-west-1;AccessKeyId=AKIA1;SecretAccessKey=abc/def+ghi", "eu-west-1", "AKIA1", null, null)]
    [InlineData("region=eu-west-1; accessKeyId=AKIA1; secretAccessKey=s; sessionToken=tok==", "eu-west-1", "AKIA1", "tok==", null)] // a token may end with "=" padding
    [InlineData("Region=us-east-1;ServiceUrl=http://localhost:4566", "us-east-1", null, null, "http://localhost:4566/")] // the default credentials
    public void Parse_ReadsTheSettings(string value, string region, string? accessKeyId, string? sessionToken, string? serviceUrl)
    {
        var parsed = AwsConnectionValue.Parse(value);

        Assert.NotNull(parsed);
        Assert.Equal((region, accessKeyId, sessionToken, serviceUrl), (parsed.Region, parsed.AccessKeyId, parsed.SessionToken, parsed.ServiceUrl?.ToString()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("AccessKeyId=AKIA1;SecretAccessKey=s")]                           // no region
    [InlineData("Region=eu-west-1;AccessKeyId=AKIA1")]                            // half a key pair
    [InlineData("Region=eu-west-1;SessionToken=t")]                               // a token without keys
    [InlineData("Region=eu-west-1;Region=us-east-1")]
    [InlineData("Region=eu-west-1;Profile=dev")]                                  // unknown setting
    [InlineData("Region=eu-west-1;ServiceUrl=localhost:4566")]                    // not an http(s) URL
    [InlineData("https://sqs.eu-west-1.amazonaws.com/123456789012/orders")]       // a queue URL isn't a connection value
    public async Task InvalidValue_FailsWithoutConnecting(string value)
    {
        var sqs = await Sqs.TestAsync(Connection(ConnectionServiceType.Sqs, value), TestContext.Current.CancellationToken);
        var sns = await Sns.TestAsync(Connection(ConnectionServiceType.Sns, value), TestContext.Current.CancellationToken);

        Assert.Equal((false, false), (sqs.Success, sns.Success));
        Assert.StartsWith("Not a valid AWS connection value", sqs.Message);
    }

    [Fact]
    public async Task UnreachableEndpoint_FailsWithoutThrowing_AndWithoutTheSecret()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var named = BrokerOptions.From([new(SnsBrokerAdapter.QueueOption, "listen")]);

        (bool, string)[] results =
        [
            ToTuple(await Sqs.TestAsync(Connection(ConnectionServiceType.Sqs, Unreachable), cancellationToken)),
            ToTuple(await Sqs.SendAsync(Connection(ConnectionServiceType.Sqs, Unreachable), "orders:send", "{}", null, cancellationToken)),
            ToTuple(await Sns.SendAsync(Connection(ConnectionServiceType.Sns, Unreachable), "placed:send", "{}", null, cancellationToken)),
            ToTuple(await Sns.ListenAsync(Connection(ConnectionServiceType.Sns, Unreachable), ChannelPattern.Parse("placed")!, TimeSpan.FromSeconds(1), null, cancellationToken)),
            ToTuple(await Sns.ListenAsync(Connection(ConnectionServiceType.Sns, Unreachable), ChannelPattern.Parse("placed")!, TimeSpan.FromSeconds(1), named, cancellationToken)),
        ];

        Assert.All(results, result =>
        {
            Assert.False(result.Item1);
            Assert.False(string.IsNullOrWhiteSpace(result.Item2));
            Assert.DoesNotContain("secret-key", result.Item2);
        });
    }

    [Fact]
    public async Task EmptyPayload_FailsWithoutConnecting()
    {
        var sqs = await Sqs.SendAsync(Connection(ConnectionServiceType.Sqs, Unreachable), "orders:send", "", null, TestContext.Current.CancellationToken);
        var sns = await Sns.SendAsync(Connection(ConnectionServiceType.Sns, Unreachable), "placed:send", null, null, TestContext.Current.CancellationToken);

        Assert.StartsWith("SQS can't send an empty message", sqs.Message);
        Assert.StartsWith("SNS can't publish an empty message", sns.Message);
    }

    [Theory]
    [InlineData("https://sqs.eu-west-1.amazonaws.com/123456789012/orders", "orders")]
    [InlineData("arn:aws:sns:eu-west-1:123456789012:placed.fifo", "placed.fifo")]
    [InlineData("placed", "placed")]
    public void NameOf_TheLastPart(string address, string name)
        => Assert.Equal(name, AwsClients.NameOf(address));

    [Theory]
    [InlineData("""{"orderId":"1"}""", """{"orderId":"1"}""")]                                                                   // raw delivery
    [InlineData("""{"Type":"Notification","TopicArn":"arn:t","Message":"{\"orderId\":\"1\"}"}""", """{"orderId":"1"}""")]   // SNS's envelope
    [InlineData("""{"Type":"Notification","TopicArn":"arn:other","Message":"x"}""", null)]                                  // another topic's
    [InlineData("plain text", "plain text")]
    public void PayloadOf_UnwrapsTheEnvelope(string body, string? payload)
        => Assert.Equal(payload, SnsBrokerAdapter.PayloadOf(body, "arn:t"));

    [Theory]
    [InlineData("placed", false)]
    [InlineData("orders.{region}", true)] // topic names have no wildcards
    public void Sns_WhyCantListen_OnlyAChannelWithParameters(string channelAddress, bool refused)
        => Assert.Equal(refused, Sns.WhyCantListen(ChannelPattern.Parse(channelAddress)!, null) is not null);

    [Fact]
    public void Options_QueueIsListenOnly_MessageGroupIdSendOnly()
    {
        var queue = Assert.Single(Sns.Options, option => option.Name == SnsBrokerAdapter.QueueOption);
        var group = Assert.Single(Sqs.Options);

        Assert.Equal((null, true), (queue.SendDescription, queue.ListenDescription is not null));
        Assert.Equal((AwsClients.MessageGroupIdOption, true, null), (group.Name, group.SendDescription is not null, group.ListenDescription));
    }

    [Fact]
    public async Task Create_ListenThroughSqs_Throws()
    {
        var (handler, specificationId, endpointId, connectionId) = await ArrangeAsync("orders:send", ConnectionServiceType.Sqs);

        var error = await Assert.ThrowsAsync<ArgumentException>(async () => await handler.Handle(
            new CreateTestScenario("Listen", specificationId, endpointId, connectionId, null, TestScenarioKind.Listen),
            TestContext.Current.CancellationToken));

        Assert.Contains("compete for its messages", error.Message);
    }

    private static (bool, string) ToTuple(Application.Abstractions.ConnectionTestResult result) => (result.Success, result.Message);

    private static (bool, string) ToTuple(Application.Abstractions.MessageSendResult result) => (result.Success, result.Message);

    private static (bool, string) ToTuple(Application.Abstractions.MessageListenResult result) => (result.Received, result.Message);

    private static async Task<(CreateTestScenarioHandler Handler, Guid SpecificationId, Guid EndpointId, Guid ConnectionId)> ArrangeAsync(string operationKey, ConnectionServiceType type)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var specifications = new FakeApiSpecificationRepository();
        var connections = new FakeConnectionRepository();
        var specificationId = Guid.NewGuid();
        var endpointId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        await specifications.UpsertAsync(new ApiSpecification
        {
            Id = specificationId, Title = "Orders", Kind = SpecificationKind.AsyncApi,
            Endpoints = [new MockEndpoint { Id = endpointId, SpecificationId = specificationId, OperationKey = operationKey }]
        }, cancellationToken);
        await connections.InsertAsync(Connection(type, Unreachable, connectionId), cancellationToken);
        return (new CreateTestScenarioHandler(new FakeTestScenarioRepository(), specifications, connections, new CronSchedule(), BrokerAdapters.Registry()), specificationId, endpointId, connectionId);
    }

    private static Connection Connection(ConnectionServiceType type, string value, Guid? id = null)
        => new() { Id = id ?? Guid.NewGuid(), Name = type.ToString(), ServiceType = type, Value = value };
}
