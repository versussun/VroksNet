using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using StackExchange.Redis;

namespace VroksNet.IntegrationTests.Brokers.Redis;

/// <summary>
/// Redis connections end-to-end against the Aspire Redis resource (N3 of the broker adapters
/// plan), in both modes: the connection test, Send scenarios (read back with StackExchange.Redis,
/// the library the adapter uses), Listen scenarios, and suites whose Send is caught by their
/// Listen. Channels and stream keys are GUID-suffixed so repeated runs don't collide.
/// </summary>
[Collection("AppHost.Redis")]
public sealed class RedisApiTests(RedisAppHostFixture fixture)
{
    [Fact]
    public async Task TestConnectionValue_Redis_Succeeds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var response = await fixture.ApiServiceClient.PostAsJsonAsync("/api/connections/test", new { ServiceType = "Redis", Value = await fixture.ConnectionValueAsync(cancellationToken) }, cancellationToken);
        var result = (await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!;

        Assert.True(result["success"]!.GetValue<bool>(), result["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task Send_RedisPubSub_PublishesToTheChannel()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var channel = $"orders.{suffix}.created";
        var scenarioId = await CreateScenarioAsync(suffix, channel, "Send", mode: null, cancellationToken);

        await using var redis = await ConnectAsync(cancellationToken);
        var queue = await redis.GetSubscriber().SubscribeAsync(RedisChannel.Literal(channel));
        var run = await RunAsync(scenarioId, cancellationToken);

        Assert.True(run["success"]!.GetValue<bool>(), run["message"]!.GetValue<string>());
        Assert.Contains("1 subscriber received it", run["message"]!.GetValue<string>());
        Assert.Contains("ord_1", (await queue.ReadAsync(cancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(10), cancellationToken)).Message.ToString());
    }

    [Fact]
    public async Task Send_RedisStream_AddsAnEntryWithThePayloadField()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var key = $"orders:{suffix}";
        var scenarioId = await CreateScenarioAsync(suffix, key, "Send", mode: "stream", cancellationToken);

        var run = await RunAsync(scenarioId, cancellationToken);

        Assert.True(run["success"]!.GetValue<bool>(), run["message"]!.GetValue<string>());
        await using var redis = await ConnectAsync(cancellationToken);
        var entry = Assert.Single(await redis.GetDatabase().StreamRangeAsync(key));
        Assert.Contains("ord_1", entry["payload"].ToString());
    }

    [Fact]
    public async Task Listen_RedisPubSub_ParameterMatchesOneSegment_AndTheMessageIsValidated()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var scenarioId = await CreateScenarioAsync(suffix, $"devices.{suffix}.{{deviceId}}", "Listen", mode: null, cancellationToken);

        await using var redis = await ConnectAsync(cancellationToken);
        var subscriber = redis.GetSubscriber();
        var run = await RunWhileSendingAsync(scenarioId, async () =>
        {
            // A glob "*" would match this one too; the adapter skips it, as it isn't one segment.
            await subscriber.PublishAsync(RedisChannel.Literal($"devices.{suffix}.d-1.extra"), """{"orderId":"wrong"}""");
            await subscriber.PublishAsync(RedisChannel.Literal($"devices.{suffix}.d-1"), """{"orderId":"ord_1"}""");
        }, cancellationToken);

        Assert.True(run["success"]!.GetValue<bool>(), run["message"]!.GetValue<string>());
        Assert.Contains($"devices.{suffix}.d-1\"", run["message"]!.GetValue<string>());
        Assert.Contains("ord_1", run["responseBody"]!.GetValue<string>());
        Assert.True(run["contractValidation"]!["isValid"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Listen_RedisStream_GetsTheNextEntry_NotAnOldOne()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var key = $"events:{suffix}";
        await using var redis = await ConnectAsync(cancellationToken);
        var database = redis.GetDatabase();
        await database.StreamAddAsync(key, "payload", """{"orderId":"old"}""");
        var scenarioId = await CreateScenarioAsync(suffix, key, "Listen", mode: "stream", cancellationToken);

        var run = await RunWhileSendingAsync(scenarioId, () => database.StreamAddAsync(key, "payload", """{"orderId":"ord_1"}"""), cancellationToken);

        Assert.True(run["success"]!.GetValue<bool>(), run["message"]!.GetValue<string>());
        Assert.Contains($"stream \"{key}\"", run["message"]!.GetValue<string>());
        Assert.Contains("ord_1", run["responseBody"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("stream")]
    public async Task Suite_Redis_ItsListenCatchesItsSend(string? mode)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var (specificationId, endpointId) = await ImportAsync(suffix, $"suite.{suffix}.created", cancellationToken);
        var connectionId = await CreateConnectionAsync(suffix, cancellationToken);
        var send = await PostIdAsync("/api/test-scenarios", Scenario($"redis-suite-send-{suffix}", specificationId, endpointId, connectionId, "Send", mode), cancellationToken);
        var listen = await PostIdAsync("/api/test-scenarios", Scenario($"redis-suite-listen-{suffix}", specificationId, endpointId, connectionId, "Listen", mode), cancellationToken);
        var suiteId = await PostIdAsync("/api/test-suites", new { Name = $"redis-suite-{suffix}", TestScenarioIds = new[] { send, listen } }, cancellationToken);

        var start = await fixture.ApiServiceClient.PostAsync($"/api/test-suites/{suiteId}/runs", null, cancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        var run = await WaitForFinalStatusAsync(start.Headers.Location!.OriginalString, cancellationToken);

        Assert.True(run["status"]!.GetValue<string>() == "Passed", run.ToJsonString());
    }

    [Fact]
    public async Task BadRedisScenarios_AreA400WithTheReason()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var (specificationId, endpointId) = await ImportAsync(suffix, $"orders.{suffix}.{{region}}", cancellationToken);
        var connectionId = await CreateConnectionAsync(suffix, cancellationToken);

        var badMode = await fixture.ApiServiceClient.PostAsJsonAsync("/api/test-scenarios",
            Scenario($"redis-bad-mode-{suffix}", specificationId, endpointId, connectionId, "Send", "queue"), cancellationToken);
        var streamWithParameters = await fixture.ApiServiceClient.PostAsJsonAsync("/api/test-scenarios",
            Scenario($"redis-stream-listen-{suffix}", specificationId, endpointId, connectionId, "Listen", "stream"), cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, badMode.StatusCode);
        Assert.Contains("can't be \"queue\"", (await badMode.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!["detail"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.BadRequest, streamWithParameters.StatusCode);
        Assert.Contains("a Redis stream is one key", (await streamWithParameters.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!["detail"]!.GetValue<string>());
    }

    private static object Scenario(string name, Guid specificationId, Guid endpointId, Guid connectionId, string kind, string? mode) => new
    {
        Name = name, SpecificationId = specificationId, MockEndpointId = endpointId, ConnectionId = connectionId, Kind = kind,
        ListenTimeoutSeconds = kind == "Listen" ? 20 : (int?)null,
        BrokerOptions = mode is null ? null : new Dictionary<string, string> { ["mode"] = mode }
    };

    private async Task<Guid> CreateScenarioAsync(string suffix, string channelAddress, string kind, string? mode, CancellationToken cancellationToken)
    {
        var (specificationId, endpointId) = await ImportAsync(suffix, channelAddress, cancellationToken);
        var connectionId = await CreateConnectionAsync(suffix, cancellationToken);
        return await PostIdAsync("/api/test-scenarios", Scenario($"redis-{kind.ToLowerInvariant()}-{suffix}", specificationId, endpointId, connectionId, kind, mode), cancellationToken);
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

    private async Task<ConnectionMultiplexer> ConnectAsync(CancellationToken cancellationToken)
        => await ConnectionMultiplexer.ConnectAsync(await fixture.ConnectionValueAsync(cancellationToken));

    private async Task<Guid> CreateConnectionAsync(string suffix, CancellationToken cancellationToken)
        => await PostIdAsync("/api/connections", new { Name = $"Redis {suffix}", ServiceType = "Redis", Value = await fixture.ConnectionValueAsync(cancellationToken) }, cancellationToken);

    /// <summary>A one-operation AsyncAPI spec ("send") on <paramref name="channelAddress"/>, whose example is <c>{"orderId":"ord_1"}</c>.</summary>
    private async Task<(Guid SpecificationId, Guid EndpointId)> ImportAsync(string suffix, string channelAddress, CancellationToken cancellationToken)
    {
        using var import = new HttpRequestMessage(HttpMethod.Post, "/api/specifications/asyncapi")
        {
            Content = new StringContent($$"""
                asyncapi: 3.0.0
                info: { title: "Redis events {{suffix}} {{channelAddress}}", version: "1" }
                servers:
                  local: { host: "localhost:6379", protocol: redis }
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
}
