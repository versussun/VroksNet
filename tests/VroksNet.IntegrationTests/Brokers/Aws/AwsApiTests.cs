using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Amazon.SimpleNotificationService.Model;
using Amazon.SQS;
using Amazon.SQS.Model;

namespace VroksNet.IntegrationTests.Brokers.Aws;

/// <summary>
/// AWS SQS and SNS connections end-to-end against LocalStack (N5 of the broker adapters plan): the
/// connection test, Send to a queue and to a topic (read back with the AWS SDK, the library the
/// adapters use), SNS Listen through a temporary queue and through a named one, the readable
/// refusals, and a suite whose Send is caught by its Listen. Queues and topics are GUID-named.
/// </summary>
[Collection("AppHost.Aws")]
public sealed class AwsApiTests(AwsAppHostFixture fixture)
{
    [Theory]
    [InlineData("Sqs")]
    [InlineData("Sns")]
    public async Task TestConnectionValue_Aws_Succeeds(string serviceType)
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var response = await fixture.ApiServiceClient.PostAsJsonAsync("/api/connections/test", new { ServiceType = serviceType, Value = fixture.ConnectionValue }, cancellationToken);
        var result = (await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!;

        Assert.True(result["success"]!.GetValue<bool>(), result["message"]!.GetValue<string>());
        Assert.Contains("account", result["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task Send_Sqs_TheQueueGetsTheMessage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        using var sqs = fixture.Sqs();
        var queueUrl = (await sqs.CreateQueueAsync($"orders-{suffix}", cancellationToken)).QueueUrl;
        var scenarioId = await CreateScenarioAsync(suffix, $"orders-{suffix}", "Sqs", "Send", queue: null, cancellationToken);

        var run = await RunAsync(scenarioId, cancellationToken);

        Assert.True(run["success"]!.GetValue<bool>(), run["message"]!.GetValue<string>());
        var received = await ReceiveAsync(sqs, queueUrl, cancellationToken);
        Assert.Contains(suffix, Assert.Single(received).Body);
    }

    [Fact]
    public async Task Send_SqsFifo_SendsInTheGivenGroup()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        using var sqs = fixture.Sqs();
        var queueUrl = (await sqs.CreateQueueAsync(new CreateQueueRequest { QueueName = $"orders-{suffix}.fifo", Attributes = new() { ["FifoQueue"] = "true" } }, cancellationToken)).QueueUrl;
        var scenarioId = await CreateScenarioAsync(suffix, $"orders-{suffix}.fifo", "Sqs", "Send", queue: null, cancellationToken, messageGroupId: "tenant-7");

        var run = await RunAsync(scenarioId, cancellationToken);

        Assert.True(run["success"]!.GetValue<bool>(), run["message"]!.GetValue<string>());
        var message = Assert.Single(await ReceiveAsync(sqs, queueUrl, cancellationToken));
        Assert.Equal("tenant-7", message.Attributes["MessageGroupId"]);
    }

    [Fact]
    public async Task Send_SqsMissingQueue_FailsReadably()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var scenarioId = await CreateScenarioAsync(suffix, $"missing-{suffix}", "Sqs", "Send", queue: null, cancellationToken);

        var run = await RunAsync(scenarioId, cancellationToken);

        Assert.False(run["success"]!.GetValue<bool>());
        Assert.Equal($"No queue \"missing-{suffix}\".", run["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task Send_Sns_ASubscribedQueueGetsTheMessage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        using var sqs = fixture.Sqs();
        var (_, queueUrl) = await CreateSubscribedQueueAsync(sqs, suffix, cancellationToken);
        var scenarioId = await CreateScenarioAsync(suffix, $"placed-{suffix}", "Sns", "Send", queue: null, cancellationToken);

        var run = await RunAsync(scenarioId, cancellationToken);

        Assert.True(run["success"]!.GetValue<bool>(), run["message"]!.GetValue<string>());
        Assert.Contains(suffix, Assert.Single(await ReceiveAsync(sqs, queueUrl, cancellationToken)).Body);
    }

    [Fact]
    public async Task Listen_SnsTemporaryQueue_GetsTheNextMessage_AndCleansUp()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        using var sns = fixture.Sns();
        using var sqs = fixture.Sqs();
        var topicArn = (await sns.CreateTopicAsync($"placed-{suffix}", cancellationToken)).TopicArn;
        var scenarioId = await CreateScenarioAsync(suffix, $"placed-{suffix}", "Sns", "Listen", queue: null, cancellationToken);

        var run = await RunWhileSendingAsync(scenarioId, () => sns.PublishAsync(topicArn, $$"""{"orderId":"{{suffix}}"}""", cancellationToken), cancellationToken);

        Assert.True(run["success"]!.GetValue<bool>(), run["message"]!.GetValue<string>());
        Assert.Contains(suffix, run["responseBody"]!.GetValue<string>());
        Assert.True(run["contractValidation"]!["isValid"]!.GetValue<bool>());
        Assert.Empty((await sns.ListSubscriptionsByTopicAsync(topicArn, cancellationToken)).Subscriptions ?? []);
        Assert.DoesNotContain((await sqs.ListQueuesAsync(new ListQueuesRequest { QueueNamePrefix = "vroksnet-" }, cancellationToken)).QueueUrls ?? [], url => url.Contains(suffix));
    }

    [Fact]
    public async Task Listen_SnsNamedQueue_GetsTheNextMessage_NotAnOldOne()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        using var sns = fixture.Sns();
        using var sqs = fixture.Sqs();
        var (topicArn, _) = await CreateSubscribedQueueAsync(sqs, suffix, cancellationToken);
        await sns.PublishAsync(topicArn, $$"""{"orderId":"old-{{suffix}}"}""", cancellationToken);
        var scenarioId = await CreateScenarioAsync(suffix, $"placed-{suffix}", "Sns", "Listen", $"listen-{suffix}", cancellationToken);

        var run = await RunWhileSendingAsync(scenarioId, () => sns.PublishAsync(topicArn, $$"""{"orderId":"new-{{suffix}}"}""", cancellationToken), cancellationToken);

        Assert.True(run["success"]!.GetValue<bool>(), run["message"]!.GetValue<string>());
        Assert.Contains($"queue \"listen-{suffix}\"", run["message"]!.GetValue<string>());
        Assert.Contains($"new-{suffix}", run["responseBody"]!.GetValue<string>());
    }

    [Fact]
    public async Task Suite_Sns_ItsListenCatchesItsSend()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        using var sns = fixture.Sns();
        await sns.CreateTopicAsync($"placed-{suffix}", cancellationToken);
        var (specificationId, endpointId) = await ImportAsync(suffix, $"placed-{suffix}", cancellationToken);
        var connectionId = await CreateConnectionAsync(suffix, "Sns", cancellationToken);
        var send = await PostIdAsync("/api/test-scenarios", Scenario($"sns-suite-send-{suffix}", specificationId, endpointId, connectionId, "Send", null), cancellationToken);
        var listen = await PostIdAsync("/api/test-scenarios", Scenario($"sns-suite-listen-{suffix}", specificationId, endpointId, connectionId, "Listen", null), cancellationToken);
        var suiteId = await PostIdAsync("/api/test-suites", new { Name = $"sns-suite-{suffix}", TestScenarioIds = new[] { send, listen } }, cancellationToken);

        var start = await fixture.ApiServiceClient.PostAsync($"/api/test-suites/{suiteId}/runs", null, cancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        var run = await WaitForFinalStatusAsync(start.Headers.Location!.OriginalString, cancellationToken);

        Assert.True(run["status"]!.GetValue<string>() == "Passed", run.ToJsonString());
    }

    [Theory]
    [InlineData("Sqs", "orders", "compete for its messages")]                 // queues are Send only
    [InlineData("Sns", "orders.{region}", "has no wildcards")]
    public async Task UnlistenableScenarios_AreA400WithTheReason(string serviceType, string channel, string reason)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var (specificationId, endpointId) = await ImportAsync(suffix, channel, cancellationToken);
        var connectionId = await CreateConnectionAsync(suffix, serviceType, cancellationToken);

        var response = await fixture.ApiServiceClient.PostAsJsonAsync("/api/test-scenarios",
            Scenario($"aws-unlistenable-{suffix}", specificationId, endpointId, connectionId, "Listen", null), cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(reason, (await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!["detail"]!.GetValue<string>());
    }

    /// <summary>A topic "placed-&lt;suffix&gt;" and a queue "listen-&lt;suffix&gt;" subscribed to it with raw delivery.</summary>
    private async Task<(string TopicArn, string QueueUrl)> CreateSubscribedQueueAsync(IAmazonSQS sqs, string suffix, CancellationToken cancellationToken)
    {
        using var sns = fixture.Sns();
        var topicArn = (await sns.CreateTopicAsync($"placed-{suffix}", cancellationToken)).TopicArn;
        var queueUrl = (await sqs.CreateQueueAsync($"listen-{suffix}", cancellationToken)).QueueUrl;
        var queueArn = (await sqs.GetQueueAttributesAsync(queueUrl, ["QueueArn"], cancellationToken)).Attributes["QueueArn"];
        await sns.SubscribeAsync(new SubscribeRequest { TopicArn = topicArn, Protocol = "sqs", Endpoint = queueArn, Attributes = new() { ["RawMessageDelivery"] = "true" } }, cancellationToken);
        return (topicArn, queueUrl);
    }

    private static async Task<List<Message>> ReceiveAsync(IAmazonSQS sqs, string queueUrl, CancellationToken cancellationToken)
        => (await sqs.ReceiveMessageAsync(new ReceiveMessageRequest { QueueUrl = queueUrl, WaitTimeSeconds = 5, MaxNumberOfMessages = 10, MessageSystemAttributeNames = ["All"] }, cancellationToken)).Messages ?? [];

    private static object Scenario(string name, Guid specificationId, Guid endpointId, Guid connectionId, string kind, Dictionary<string, string>? brokerOptions) => new
    {
        Name = name, SpecificationId = specificationId, MockEndpointId = endpointId, ConnectionId = connectionId, Kind = kind,
        ListenTimeoutSeconds = kind == "Listen" ? 20 : (int?)null,
        BrokerOptions = brokerOptions
    };

    private async Task<Guid> CreateScenarioAsync(string suffix, string channelAddress, string serviceType, string kind, string? queue, CancellationToken cancellationToken, string? messageGroupId = null)
    {
        var (specificationId, endpointId) = await ImportAsync(suffix, channelAddress, cancellationToken);
        var connectionId = await CreateConnectionAsync(suffix, serviceType, cancellationToken);
        var options = new Dictionary<string, string>();
        if (queue is not null)
        {
            options["queue"] = queue;
        }

        if (messageGroupId is not null)
        {
            options["messageGroupId"] = messageGroupId;
        }

        return await PostIdAsync("/api/test-scenarios", Scenario($"aws-{kind.ToLowerInvariant()}-{suffix}", specificationId, endpointId, connectionId, kind, options.Count == 0 ? null : options), cancellationToken);
    }

    /// <summary>
    /// Starts the run, then sends every 250ms until it returns, so a message lands whenever its
    /// subscription goes live. A plain HttpClient: the fixture's resilience handler would time a
    /// long run out at 10s and retry it.
    /// </summary>
    private async Task<JsonNode> RunWhileSendingAsync(Guid scenarioId, Func<Task> sendAsync, CancellationToken cancellationToken)
    {
        using var runClient = new HttpClient { BaseAddress = fixture.ApiServiceHttpAddress, Timeout = TimeSpan.FromMinutes(2) };
        var runTask = runClient.PostAsync($"/api/test-scenarios/{scenarioId}/run", null, cancellationToken);
        while (!runTask.IsCompleted)
        {
            await sendAsync();
            await Task.WhenAny(runTask, Task.Delay(250, cancellationToken));
        }

        var response = await runTask;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!;
    }

    private async Task<JsonNode> RunAsync(Guid scenarioId, CancellationToken cancellationToken)
    {
        using var runClient = new HttpClient { BaseAddress = fixture.ApiServiceHttpAddress, Timeout = TimeSpan.FromMinutes(2) };
        var response = await runClient.PostAsync($"/api/test-scenarios/{scenarioId}/run", null, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!;
    }

    private async Task<JsonNode> WaitForFinalStatusAsync(string path, CancellationToken cancellationToken)
    {
        string[] finalStatuses = ["Passed", "Failed", "Cancelled", "Interrupted"];
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (true)
        {
            var run = (await fixture.ApiServiceClient.GetFromJsonAsync<JsonNode>(path, cancellationToken))!;
            if (finalStatuses.Contains(run["status"]!.GetValue<string>()))
            {
                return run;
            }

            Assert.True(DateTime.UtcNow < deadline, $"Still {run["status"]}: {run.ToJsonString()}");
            await Task.Delay(250, cancellationToken);
        }
    }

    private async Task<Guid> CreateConnectionAsync(string suffix, string serviceType, CancellationToken cancellationToken)
        => await PostIdAsync("/api/connections", new { Name = $"{serviceType} {suffix}", ServiceType = serviceType, Value = fixture.ConnectionValue }, cancellationToken);

    /// <summary>A one-operation AsyncAPI spec ("send") on <paramref name="channelAddress"/>, whose example is <c>{"orderId":"&lt;suffix&gt;"}</c>.</summary>
    private async Task<(Guid SpecificationId, Guid EndpointId)> ImportAsync(string suffix, string channelAddress, CancellationToken cancellationToken)
    {
        using var import = new HttpRequestMessage(HttpMethod.Post, "/api/specifications/asyncapi")
        {
            Content = new StringContent($$"""
                asyncapi: 3.0.0
                info: { title: "AWS events {{suffix}} {{channelAddress}}", version: "1" }
                servers:
                  local: { host: "localhost:4566", protocol: sns }
                channels:
                  events:
                    address: "{{channelAddress}}"
                    messages:
                      event:
                        payload:
                          type: object
                          required: [orderId]
                          properties: { orderId: { type: string } }
                        examples:
                          - payload: { orderId: "{{suffix}}" }
                operations:
                  publishEvent:
                    action: send
                    channel: { $ref: "#/channels/events" }
                    messages:
                      - $ref: "#/channels/events/messages/event"
                """, Encoding.UTF8, "text/plain")
        };
        var imported = await fixture.ApiServiceClient.SendAsync(import, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, imported.StatusCode);
        var specificationId = (await imported.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!["id"]!.GetValue<Guid>();
        var endpointId = (await fixture.ApiServiceClient.GetFromJsonAsync<JsonNode>($"/api/specifications/{specificationId}", cancellationToken))!["endpoints"]!.AsArray().Single()!["id"]!.GetValue<Guid>();
        return (specificationId, endpointId);
    }

    private async Task<Guid> PostIdAsync(string path, object body, CancellationToken cancellationToken)
    {
        var response = await fixture.ApiServiceClient.PostAsJsonAsync(path, body, cancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync(cancellationToken));
        return (await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!["id"]!.GetValue<Guid>();
    }
}
