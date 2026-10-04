using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Azure.Messaging.ServiceBus;

namespace VroksNet.IntegrationTests.Brokers.ServiceBus;

/// <summary>
/// Azure Service Bus connections end-to-end against the Aspire emulator (N4 of the broker adapters
/// plan): the connection test, Send to a queue and a topic (read back with
/// Azure.Messaging.ServiceBus, the library the adapter uses), Listen through a named
/// subscription, the readable refusals, and a suite whose Send is caught by its Listen. The
/// emulator only has the entities AppHost declares, so every test shares them; each message
/// carries the test's own id, which is what the assertions look for.
/// </summary>
[Collection("AppHost.ServiceBus")]
public sealed class ServiceBusApiTests(ServiceBusAppHostFixture fixture)
{
    private const string Queue = ServiceBusAppHostFixture.Queue;
    private const string Topic = ServiceBusAppHostFixture.Topic;
    private const string Subscription = ServiceBusAppHostFixture.Subscription;

    [Fact]
    public async Task TestConnectionValue_ServiceBus_Succeeds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var result = await TestConnectionValueAsync(await fixture.ConnectionValueAsync(cancellationToken), cancellationToken);

        Assert.True(result["success"]!.GetValue<bool>(), result["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task Send_ServiceBusQueue_TheQueueGetsTheMessage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var scenarioId = await CreateScenarioAsync(suffix, Queue, "Send", subscription: null, cancellationToken);

        var run = await RunAsync(scenarioId, cancellationToken);

        Assert.True(run["success"]!.GetValue<bool>(), run["message"]!.GetValue<string>());
        await using var client = new ServiceBusClient(await fixture.ConnectionValueAsync(cancellationToken));
        await using var receiver = client.CreateReceiver(Queue, new ServiceBusReceiverOptions { ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete });
        Assert.True(await ReceiveContainingAsync(receiver, suffix, cancellationToken), $"No message with {suffix} on queue {Queue}.");
    }

    [Fact]
    public async Task Send_ServiceBusTopic_Succeeds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var scenarioId = await CreateScenarioAsync(suffix, Topic, "Send", subscription: null, cancellationToken);

        var run = await RunAsync(scenarioId, cancellationToken);

        // What it leaves in the "vroksnet" subscription is cleared by the next Listen on it.
        Assert.True(run["success"]!.GetValue<bool>(), run["message"]!.GetValue<string>());
        Assert.Contains(Topic, run["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task Send_MissingEntity_FailsReadably()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var scenarioId = await CreateScenarioAsync(suffix, $"missing.{suffix}", "Send", subscription: null, cancellationToken);

        var run = await RunAsync(scenarioId, cancellationToken);

        Assert.False(run["success"]!.GetValue<bool>());
        Assert.Contains($"\"missing.{suffix}\"", run["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task Listen_NamedSubscription_GetsTheNextMessage_NotAWaitingOne()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        await using var client = new ServiceBusClient(await fixture.ConnectionValueAsync(cancellationToken));
        await using var sender = client.CreateSender(Topic);
        await sender.SendMessageAsync(new ServiceBusMessage($$"""{"orderId":"old-{{suffix}}"}"""), cancellationToken);
        var scenarioId = await CreateScenarioAsync(suffix, Topic, "Listen", Subscription, cancellationToken);

        var run = await RunWhileSendingAsync(scenarioId, () => sender.SendMessageAsync(new ServiceBusMessage($$"""{"orderId":"new-{{suffix}}"}"""), cancellationToken), cancellationToken);

        Assert.True(run["success"]!.GetValue<bool>(), run["message"]!.GetValue<string>());
        Assert.Contains($"subscription \"{Subscription}\"", run["message"]!.GetValue<string>());
        Assert.Contains($"new-{suffix}", run["responseBody"]!.GetValue<string>());
        Assert.True(run["contractValidation"]!["isValid"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Listen_TemporarySubscription_OnTheEmulator_SaysToNameOne()
    {
        // Creating a temporary subscription needs the namespace's management endpoint, which the
        // emulator doesn't serve where its connection string points.
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var scenarioId = await CreateScenarioAsync(suffix, Topic, "Listen", subscription: null, cancellationToken);

        var run = await RunAsync(scenarioId, cancellationToken);

        Assert.False(run["success"]!.GetValue<bool>());
        Assert.Contains("name an existing subscription in the broker option \"subscription\"", run["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task Listen_Queue_FailsReadably()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var scenarioId = await CreateScenarioAsync(suffix, Queue, "Listen", Subscription, cancellationToken);

        var run = await RunAsync(scenarioId, cancellationToken);

        Assert.False(run["success"]!.GetValue<bool>());
        Assert.Contains("it can only be sent to", run["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task Listen_MissingSubscription_FailsReadably()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var scenarioId = await CreateScenarioAsync(suffix, Topic, "Listen", $"missing-{suffix}", cancellationToken);

        var run = await RunAsync(scenarioId, cancellationToken);

        Assert.False(run["success"]!.GetValue<bool>());
        Assert.Contains($"subscription \"missing-{suffix}\"", run["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task Suite_ServiceBus_ItsListenCatchesItsSend()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var (specificationId, endpointId) = await ImportAsync(suffix, Topic, cancellationToken);
        var connectionId = await CreateConnectionAsync(suffix, cancellationToken);
        var send = await PostIdAsync("/api/test-scenarios", Scenario($"servicebus-suite-send-{suffix}", specificationId, endpointId, connectionId, "Send", null), cancellationToken);
        var listen = await PostIdAsync("/api/test-scenarios", Scenario($"servicebus-suite-listen-{suffix}", specificationId, endpointId, connectionId, "Listen", Subscription), cancellationToken);
        var suiteId = await PostIdAsync("/api/test-suites", new { Name = $"servicebus-suite-{suffix}", TestScenarioIds = new[] { send, listen } }, cancellationToken);

        var start = await fixture.ApiServiceClient.PostAsync($"/api/test-suites/{suiteId}/runs", null, cancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        var run = await WaitForFinalStatusAsync(start.Headers.Location!.OriginalString, cancellationToken);

        Assert.True(run["status"]!.GetValue<string>() == "Passed", run.ToJsonString());
    }

    [Fact]
    public async Task ListenOnAChannelWithParameters_IsA400WithTheReason()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var (specificationId, endpointId) = await ImportAsync(suffix, "warehouse.{site}.changed", cancellationToken);
        var connectionId = await CreateConnectionAsync(suffix, cancellationToken);

        var response = await fixture.ApiServiceClient.PostAsJsonAsync("/api/test-scenarios",
            Scenario($"servicebus-parameters-{suffix}", specificationId, endpointId, connectionId, "Listen", Subscription), cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("has no wildcards", (await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!["detail"]!.GetValue<string>());
    }

    private static object Scenario(string name, Guid specificationId, Guid endpointId, Guid connectionId, string kind, string? subscription) => new
    {
        Name = name, SpecificationId = specificationId, MockEndpointId = endpointId, ConnectionId = connectionId, Kind = kind,
        ListenTimeoutSeconds = kind == "Listen" ? 20 : (int?)null,
        BrokerOptions = subscription is null ? null : new Dictionary<string, string> { ["subscription"] = subscription }
    };

    private async Task<JsonNode> TestConnectionValueAsync(string value, CancellationToken cancellationToken)
    {
        var response = await fixture.ApiServiceClient.PostAsJsonAsync("/api/connections/test", new { ServiceType = "ServiceBus", Value = value }, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!;
    }

    /// <summary>Receives (and removes) messages until one contains <paramref name="marker"/>, or none is left.</summary>
    private static async Task<bool> ReceiveContainingAsync(ServiceBusReceiver receiver, string marker, CancellationToken cancellationToken)
    {
        while (await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(5), cancellationToken) is { } message)
        {
            if (message.Body.ToString().Contains(marker, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<Guid> CreateScenarioAsync(string suffix, string channelAddress, string kind, string? subscription, CancellationToken cancellationToken)
    {
        var (specificationId, endpointId) = await ImportAsync(suffix, channelAddress, cancellationToken);
        var connectionId = await CreateConnectionAsync(suffix, cancellationToken);
        return await PostIdAsync("/api/test-scenarios", Scenario($"servicebus-{kind.ToLowerInvariant()}-{suffix}", specificationId, endpointId, connectionId, kind, subscription), cancellationToken);
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

    private async Task<Guid> CreateConnectionAsync(string suffix, CancellationToken cancellationToken)
        => await PostIdAsync("/api/connections", new { Name = $"Service Bus {suffix}", ServiceType = "ServiceBus", Value = await fixture.ConnectionValueAsync(cancellationToken) }, cancellationToken);

    /// <summary>A one-operation AsyncAPI spec ("send") on <paramref name="channelAddress"/>, whose example is <c>{"orderId":"&lt;suffix&gt;"}</c>.</summary>
    private async Task<(Guid SpecificationId, Guid EndpointId)> ImportAsync(string suffix, string channelAddress, CancellationToken cancellationToken)
    {
        using var import = new HttpRequestMessage(HttpMethod.Post, "/api/specifications/asyncapi")
        {
            Content = new StringContent($$"""
                asyncapi: 3.0.0
                info: { title: "Service Bus events {{suffix}} {{channelAddress}}", version: "1" }
                servers:
                  local: { host: "localhost:5672", protocol: servicebus }
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
