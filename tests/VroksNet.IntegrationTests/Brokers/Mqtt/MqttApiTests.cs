using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using MQTTnet;

namespace VroksNet.IntegrationTests.Brokers.Mqtt;

/// <summary>
/// MQTT connections end-to-end against the Mosquitto container (N2 of the broker adapters plan):
/// the connection test, a Send scenario (read back with MQTTnet, the library the adapter publishes
/// with), Listen scenarios, and a suite whose Send is caught by its Listen. Topics are
/// GUID-suffixed so repeated runs don't collide.
/// </summary>
[Collection("AppHost.Mqtt")]
public sealed class MqttApiTests(MqttAppHostFixture fixture)
{
    [Fact]
    public async Task TestConnectionValue_Mqtt_Succeeds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var response = await fixture.ApiServiceClient.PostAsJsonAsync("/api/connections/test", new { ServiceType = "Mqtt", Value = fixture.ConnectionValue }, cancellationToken);
        var result = (await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!;

        Assert.True(result["success"]!.GetValue<bool>(), result["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task Send_Mqtt_AtQos0_PublishesToTheTopic()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var topic = $"orders/{suffix}/created";
        var (specificationId, endpointId) = await ImportAsync(suffix, topic, cancellationToken);
        var connectionId = await CreateConnectionAsync(suffix, cancellationToken);
        var scenarioId = await PostIdAsync("/api/test-scenarios", new
        {
            Name = $"mqtt-send-{suffix}", SpecificationId = specificationId, MockEndpointId = endpointId, ConnectionId = connectionId, Kind = "Send",
            BrokerOptions = new Dictionary<string, string> { ["qos"] = "0" }
        }, cancellationToken);

        using var subscriber = await SubscribeAsync(topic, cancellationToken);
        var run = await RunAsync(scenarioId, cancellationToken);

        Assert.True(run["success"]!.GetValue<bool>(), run["message"]!.GetValue<string>());
        Assert.Contains("QoS 0", run["message"]!.GetValue<string>());
        Assert.Contains("ord_1", await subscriber.Received.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken));
    }

    [Fact]
    public async Task Listen_Mqtt_ParameterMatchesOneLevel_AndTheMessageIsValidated()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var (specificationId, endpointId) = await ImportAsync(suffix, $"devices/{suffix}/{{deviceId}}", cancellationToken);
        var connectionId = await CreateConnectionAsync(suffix, cancellationToken);
        var scenarioId = await PostIdAsync("/api/test-scenarios", new
        {
            Name = $"mqtt-listen-{suffix}", SpecificationId = specificationId, MockEndpointId = endpointId, ConnectionId = connectionId, Kind = "Listen", ListenTimeoutSeconds = 20
        }, cancellationToken);

        var run = await RunWhilePublishingAsync(scenarioId, $"devices/{suffix}/d-1", cancellationToken);

        Assert.True(run["success"]!.GetValue<bool>(), run["message"]!.GetValue<string>());
        Assert.Contains($"devices/{suffix}/d-1", run["message"]!.GetValue<string>());
        Assert.True(run["contractValidation"]!["isValid"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Listen_Mqtt_IgnoresARetainedMessage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var topic = $"retained/{suffix}";
        await PublishAsync(topic, """{"orderId":"old"}""", retain: true, cancellationToken);
        var (specificationId, endpointId) = await ImportAsync(suffix, topic, cancellationToken);
        var connectionId = await CreateConnectionAsync(suffix, cancellationToken);
        var scenarioId = await PostIdAsync("/api/test-scenarios", new
        {
            Name = $"mqtt-retained-{suffix}", SpecificationId = specificationId, MockEndpointId = endpointId, ConnectionId = connectionId, Kind = "Listen", ListenTimeoutSeconds = 2
        }, cancellationToken);

        var run = await RunAsync(scenarioId, cancellationToken);

        // The retained message is an old one, not the next: nothing new arrives, so the run fails.
        Assert.False(run["success"]!.GetValue<bool>());
        Assert.Contains("No message", run["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task Suite_Mqtt_ItsListenCatchesItsSend()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var (specificationId, endpointId) = await ImportAsync(suffix, $"suite/{suffix}/created", cancellationToken);
        var connectionId = await CreateConnectionAsync(suffix, cancellationToken);
        var send = await PostIdAsync("/api/test-scenarios", new { Name = $"mqtt-suite-send-{suffix}", SpecificationId = specificationId, MockEndpointId = endpointId, ConnectionId = connectionId, Kind = "Send" }, cancellationToken);
        var listen = await PostIdAsync("/api/test-scenarios", new { Name = $"mqtt-suite-listen-{suffix}", SpecificationId = specificationId, MockEndpointId = endpointId, ConnectionId = connectionId, Kind = "Listen", ListenTimeoutSeconds = 20 }, cancellationToken);
        var suiteId = await PostIdAsync("/api/test-suites", new { Name = $"mqtt-suite-{suffix}", TestScenarioIds = new[] { send, listen } }, cancellationToken);

        var start = await fixture.ApiServiceClient.PostAsync($"/api/test-suites/{suiteId}/runs", null, cancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        var run = await WaitForFinalStatusAsync(start.Headers.Location!.OriginalString, cancellationToken);

        Assert.True(run["status"]!.GetValue<string>() == "Passed", run.ToJsonString());
    }

    [Fact]
    public async Task BadMqttScenarios_AreA400WithTheReason()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var (specificationId, endpointId) = await ImportAsync(suffix, $"orders.{suffix}.{{region}}", cancellationToken);
        var connectionId = await CreateConnectionAsync(suffix, cancellationToken);

        var badQos = await fixture.ApiServiceClient.PostAsJsonAsync("/api/test-scenarios", new
        {
            Name = $"mqtt-bad-qos-{suffix}", SpecificationId = specificationId, MockEndpointId = endpointId, ConnectionId = connectionId, Kind = "Send",
            BrokerOptions = new Dictionary<string, string> { ["qos"] = "3" }
        }, cancellationToken);
        var dottedListen = await fixture.ApiServiceClient.PostAsJsonAsync("/api/test-scenarios", new
        {
            Name = $"mqtt-dotted-{suffix}", SpecificationId = specificationId, MockEndpointId = endpointId, ConnectionId = connectionId, Kind = "Listen"
        }, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, badQos.StatusCode);
        Assert.Contains("can't be \"3\"", (await badQos.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!["detail"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.BadRequest, dottedListen.StatusCode);
        Assert.Contains("MQTT wildcards", (await dottedListen.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!["detail"]!.GetValue<string>());
    }

    /// <summary>
    /// Starts the run, then publishes every 250ms until it returns, so a message lands whenever its
    /// subscription goes live. A plain HttpClient: the fixture's resilience handler would time a
    /// long run out at 10s and retry it.
    /// </summary>
    private async Task<JsonNode> RunWhilePublishingAsync(Guid scenarioId, string topic, CancellationToken cancellationToken)
    {
        using var runClient = new HttpClient { BaseAddress = fixture.ApiServiceHttpAddress, Timeout = TimeSpan.FromMinutes(2) };
        var runTask = runClient.PostAsync($"/api/test-scenarios/{scenarioId}/run", null, cancellationToken);
        while (!runTask.IsCompleted)
        {
            await PublishAsync(topic, """{"orderId":"ord_1"}""", retain: false, cancellationToken);
            await Task.WhenAny(runTask, Task.Delay(250, cancellationToken));
        }

        var response = await runTask;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!;
    }

    private async Task<JsonNode> RunAsync(Guid scenarioId, CancellationToken cancellationToken)
    {
        var response = await fixture.ApiServiceClient.PostAsync($"/api/test-scenarios/{scenarioId}/run", null, cancellationToken);
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

    private async Task PublishAsync(string topic, string payload, bool retain, CancellationToken cancellationToken)
    {
        using var client = await ConnectAsync(cancellationToken);
        await client.PublishAsync(new MqttApplicationMessageBuilder().WithTopic(topic).WithPayload(payload).WithRetainFlag(retain)
            .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce).Build(), cancellationToken);
        await client.DisconnectAsync(cancellationToken: cancellationToken);
    }

    private async Task<Subscriber> SubscribeAsync(string topic, CancellationToken cancellationToken)
    {
        var client = await ConnectAsync(cancellationToken);
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.ApplicationMessageReceivedAsync += args =>
        {
            received.TrySetResult(args.ApplicationMessage.ConvertPayloadToString());
            return Task.CompletedTask;
        };
        await client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder().WithTopicFilter(topic).Build(), cancellationToken);
        return new Subscriber(client, received.Task);
    }

    private async Task<IMqttClient> ConnectAsync(CancellationToken cancellationToken)
    {
        var endpoint = fixture.App.GetEndpoint("mqtt", "mqtt");
        var client = new MqttClientFactory().CreateMqttClient();
        await client.ConnectAsync(new MqttClientOptionsBuilder().WithTcpServer(endpoint.Host, endpoint.Port).Build(), cancellationToken);
        return client;
    }

    private async Task<Guid> CreateConnectionAsync(string suffix, CancellationToken cancellationToken)
        => await PostIdAsync("/api/connections", new { Name = $"MQTT {suffix}", ServiceType = "Mqtt", Value = fixture.ConnectionValue }, cancellationToken);

    /// <summary>A one-operation AsyncAPI spec ("send") on <paramref name="channelAddress"/>, whose example is <c>{"orderId":"ord_1"}</c>.</summary>
    private async Task<(Guid SpecificationId, Guid EndpointId)> ImportAsync(string suffix, string channelAddress, CancellationToken cancellationToken)
    {
        using var import = new HttpRequestMessage(HttpMethod.Post, "/api/specifications/asyncapi")
        {
            Content = new StringContent($$"""
                asyncapi: 3.0.0
                info: { title: "MQTT events {{suffix}} {{channelAddress}}", version: "1" }
                servers:
                  local: { host: "localhost:1883", protocol: mqtt }
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
                          - payload: { orderId: "ord_1" }
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

    private sealed class Subscriber(IMqttClient client, Task<string> received) : IDisposable
    {
        public Task<string> Received => received;

        public void Dispose() => client.Dispose();
    }
}
