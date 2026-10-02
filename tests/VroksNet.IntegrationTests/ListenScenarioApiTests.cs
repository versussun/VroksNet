using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using NATS.Client.Core;
using RabbitMQ.Client;
using VroksNet.IntegrationTests.Fixtures;

namespace VroksNet.IntegrationTests;

/// <summary>
/// Listen-mode Test Scenarios ("Фаза C") against the real RabbitMQ/NATS containers AppHost boots:
/// a run waits for a message on the operation's channel and validates it against the AsyncAPI
/// payload schema. The test can't know exactly when the run's subscription is live, so it keeps
/// publishing until the run returns — the listener takes the first message it sees.
/// </summary>
[Collection("AppHost")]
public sealed class ListenScenarioApiTests(AppHostFixture fixture)
{
    [Theory]
    [InlineData("""{"orderId":"ord_1"}""", true)]
    [InlineData("""{"orderId":42}""", false)]
    public async Task Listen_RabbitMq_ReceivesFromExchangeAndValidates(string message, bool expectValid)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = fixture.ApiServiceClient;
        var suffix = Guid.NewGuid().ToString("N");
        var routingKey = $"orders.created.{suffix}";

        var connectionString = await fixture.App.GetConnectionStringAsync("rabbitmq", cancellationToken);
        Assert.NotNull(connectionString);
        var scenarioId = await CreateListenScenarioAsync(client, suffix, routingKey, "RabbitMq", connectionString, exchange: "amq.topic", timeoutSeconds: 20, cancellationToken);

        var factory = new ConnectionFactory { Uri = new Uri(connectionString) };
        await using var rabbit = await factory.CreateConnectionAsync(cancellationToken);
        await using var channel = await rabbit.CreateChannelAsync(cancellationToken: cancellationToken);

        var run = await RunWhilePublishingAsync(scenarioId,
            () => channel.BasicPublishAsync("amq.topic", routingKey, Encoding.UTF8.GetBytes(message), cancellationToken).AsTask(),
            cancellationToken);

        Assert.Equal(expectValid, run["success"]!.GetValue<bool>());
        Assert.Equal(expectValid, run["contractValidation"]!["isValid"]!.GetValue<bool>());
        Assert.Equal(message, run["responseBody"]!.GetValue<string>());
    }

    [Fact]
    public async Task Listen_RabbitMq_HearsASendScenarioOnTheSameExchange()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = fixture.ApiServiceClient;
        var suffix = Guid.NewGuid().ToString("N");

        var connectionString = await fixture.App.GetConnectionStringAsync("rabbitmq", cancellationToken);
        Assert.NotNull(connectionString);
        var listen = await CreateListenScenarioWithTargetAsync(client, suffix, $"orders.created.{suffix}", "RabbitMq", connectionString, exchange: "amq.topic", timeoutSeconds: 20, cancellationToken);
        var sendId = await CreateSendScenarioAsync(client, suffix, listen.SpecificationId, listen.MockEndpointId, listen.ConnectionId, exchange: "amq.topic", cancellationToken);

        var run = await RunWhilePublishingAsync(listen.ScenarioId, async () =>
        {
            var send = await client.PostAsync($"/api/test-scenarios/{sendId}/run", null, cancellationToken);
            var result = (await send.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!;
            Assert.True(result["success"]!.GetValue<bool>(), result["message"]?.GetValue<string>());
        }, cancellationToken);

        Assert.True(run["success"]!.GetValue<bool>());
        Assert.True(run["contractValidation"]!["isValid"]!.GetValue<bool>());
        Assert.Equal("""{"orderId":"ord_1"}""", run["responseBody"]!.GetValue<string>());
    }

    [Fact]
    public async Task Send_RabbitMq_ToAMissingExchange_FailsReadably()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = fixture.ApiServiceClient;
        var suffix = Guid.NewGuid().ToString("N");

        var connectionString = await fixture.App.GetConnectionStringAsync("rabbitmq", cancellationToken);
        Assert.NotNull(connectionString);
        var listen = await CreateListenScenarioWithTargetAsync(client, suffix, $"orders.created.{suffix}", "RabbitMq", connectionString, exchange: null, timeoutSeconds: 1, cancellationToken);
        var sendId = await CreateSendScenarioAsync(client, suffix, listen.SpecificationId, listen.MockEndpointId, listen.ConnectionId, exchange: $"missing-{suffix}", cancellationToken);

        var response = await client.PostAsync($"/api/test-scenarios/{sendId}/run", null, cancellationToken);
        var run = (await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!;

        Assert.False(run["success"]!.GetValue<bool>());
        Assert.Contains($"Exchange \"missing-{suffix}\" doesn't exist", run["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task Listen_Nats_ReceivesOnSubject()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = fixture.ApiServiceClient;
        var suffix = Guid.NewGuid().ToString("N");
        var subject = $"orders.created.{suffix}";

        var connectionString = await fixture.App.GetConnectionStringAsync("nats", cancellationToken);
        Assert.NotNull(connectionString);
        var scenarioId = await CreateListenScenarioAsync(client, suffix, subject, "Nats", connectionString, exchange: null, timeoutSeconds: 20, cancellationToken);

        await using var nats = new NatsConnection(new NatsOpts { Url = connectionString });
        var run = await RunWhilePublishingAsync(scenarioId,
            () => nats.PublishAsync(subject, """{"orderId":"ord_1"}""", cancellationToken: cancellationToken).AsTask(),
            cancellationToken);

        Assert.True(run["success"]!.GetValue<bool>());
        Assert.True(run["contractValidation"]!["isValid"]!.GetValue<bool>());

        // Logged as an inbound broker message.
        var history = await client.GetFromJsonAsync<JsonNode>($"/api/call-records?testScenarioId={scenarioId}", cancellationToken);
        Assert.Equal("InboundBrokerMessage", Assert.Single(history!["items"]!.AsArray())!["direction"]!.GetValue<string>());
    }

    [Fact]
    public async Task Listen_NothingPublished_TimesOutAsAFailedRun()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = fixture.ApiServiceClient;
        var suffix = Guid.NewGuid().ToString("N");

        var connectionString = await fixture.App.GetConnectionStringAsync("rabbitmq", cancellationToken);
        Assert.NotNull(connectionString);
        var scenarioId = await CreateListenScenarioAsync(client, suffix, $"silent.{suffix}", "RabbitMq", connectionString, exchange: null, timeoutSeconds: 1, cancellationToken);

        var response = await client.PostAsync($"/api/test-scenarios/{scenarioId}/run", null, cancellationToken);
        var run = (await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!;

        Assert.False(run["success"]!.GetValue<bool>());
        Assert.Contains("No message", run["message"]!.GetValue<string>());
        Assert.Null(run["contractValidation"]);
    }

    [Fact]
    public async Task Listen_Nats_ChannelParameterMatchesAnyValueInThatSegment()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = fixture.ApiServiceClient;
        var suffix = Guid.NewGuid().ToString("N");

        var connectionString = await fixture.App.GetConnectionStringAsync("nats", cancellationToken);
        Assert.NotNull(connectionString);
        // "{region}" is a whole "."-separated segment, so the run subscribes to "listen.{suffix}.*".
        var scenarioId = await CreateListenScenarioAsync(client, suffix, $"listen.{suffix}.{{region}}", "Nats", connectionString, exchange: null, timeoutSeconds: 20, cancellationToken);

        await using var nats = new NatsConnection(new NatsOpts { Url = connectionString });
        var run = await RunWhilePublishingAsync(scenarioId,
            () => nats.PublishAsync($"listen.{suffix}.eu", """{"orderId":"ord_1"}""", cancellationToken: cancellationToken).AsTask(),
            cancellationToken);

        Assert.True(run["success"]!.GetValue<bool>());
        Assert.Contains($"listen.{suffix}.eu", run["message"]!.GetValue<string>());
    }

    /// <summary>
    /// Starts the run, then publishes every 250ms until it returns — so the message arrives whenever
    /// the run's subscription goes live. The run goes through its own plain HttpClient: the fixture's
    /// client has a resilience handler that would time out a long run at 10s and retry the POST,
    /// starting a second run.
    /// </summary>
    private async Task<JsonNode> RunWhilePublishingAsync(Guid scenarioId, Func<Task> publish, CancellationToken cancellationToken)
    {
        using var runClient = new HttpClient { BaseAddress = fixture.ApiServiceHttpAddress, Timeout = TimeSpan.FromMinutes(2) };
        var runTask = runClient.PostAsync($"/api/test-scenarios/{scenarioId}/run", null, cancellationToken);
        try
        {
            while (!runTask.IsCompleted)
            {
                await publish();
                await Task.WhenAny(runTask, Task.Delay(250, cancellationToken));
            }
        }
        finally
        {
            // Never leave the run unobserved, even if a publish threw.
            await Task.WhenAny(runTask);
        }

        var response = await runTask;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!;
    }

    private static async Task<Guid> CreateListenScenarioAsync(
        HttpClient client, string suffix, string channelAddress, string serviceType, string connectionValue, string? exchange, int timeoutSeconds, CancellationToken cancellationToken)
        => (await CreateListenScenarioWithTargetAsync(client, suffix, channelAddress, serviceType, connectionValue, exchange, timeoutSeconds, cancellationToken)).ScenarioId;

    /// <summary>Also returns what the scenario targets, so a test can add a second scenario on the same operation and connection.</summary>
    private static async Task<(Guid ScenarioId, Guid SpecificationId, Guid MockEndpointId, Guid ConnectionId)> CreateListenScenarioWithTargetAsync(
        HttpClient client, string suffix, string channelAddress, string serviceType, string connectionValue, string? exchange, int timeoutSeconds, CancellationToken cancellationToken)
    {
        var yaml = $"""
            asyncapi: 3.0.0
            info:
              title: "Listen Fixture {suffix}"
              version: "1.0.0"
            channels:
              orderCreated:
                address: {channelAddress}
                messages:
                  orderCreated:
                    $ref: "#/components/messages/OrderCreated"
            operations:
              publishOrderCreated:
                action: send
                channel:
                  $ref: "#/channels/orderCreated"
                messages:
                  - $ref: "#/channels/orderCreated/messages/orderCreated"
            components:
              messages:
                OrderCreated:
                  payload:
                    $ref: "#/components/schemas/OrderCreatedPayload"
              schemas:
                OrderCreatedPayload:
                  type: object
                  required: [orderId]
                  properties:
                    orderId:
                      type: string
            """;

        using var importRequest = new HttpRequestMessage(HttpMethod.Post, "/api/specifications/asyncapi")
        {
            Content = new StringContent(yaml, Encoding.UTF8, "text/plain"),
        };
        importRequest.Headers.Add("X-File-Name", "listen.yaml");
        var importResponse = await client.SendAsync(importRequest, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, importResponse.StatusCode);
        var specificationId = (await importResponse.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!["id"]!.GetValue<Guid>();

        var details = await client.GetFromJsonAsync<JsonNode>($"/api/specifications/{specificationId}", cancellationToken);
        var endpoint = details!["endpoints"]!.AsArray().Single()!;
        Assert.Equal("Listen", endpoint["defaultTestScenarioKind"]!.GetValue<string>()); // a "send" operation defaults to Listen

        var connectionResponse = await client.PostAsJsonAsync(
            "/api/connections",
            new { Name = $"Listen Connection {serviceType} {suffix}", ServiceType = serviceType, Value = connectionValue },
            cancellationToken);
        var connectionId = (await connectionResponse.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!["id"]!.GetValue<Guid>();

        var scenarioResponse = await client.PostAsJsonAsync(
            "/api/test-scenarios",
            new
            {
                Name = $"Listen Scenario {suffix}",
                SpecificationId = specificationId,
                MockEndpointId = endpoint["id"]!.GetValue<Guid>(),
                ConnectionId = connectionId,
                PayloadOverride = (string?)null,
                Kind = "Listen",
                ListenTimeoutSeconds = timeoutSeconds,
                Exchange = exchange
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, scenarioResponse.StatusCode);
        var scenarioId = (await scenarioResponse.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!["id"]!.GetValue<Guid>();
        return (scenarioId, specificationId, endpoint["id"]!.GetValue<Guid>(), connectionId);
    }

    private static async Task<Guid> CreateSendScenarioAsync(HttpClient client, string suffix, Guid specificationId, Guid mockEndpointId, Guid connectionId, string? exchange, CancellationToken cancellationToken)
    {
        var response = await client.PostAsJsonAsync(
            "/api/test-scenarios",
            new
            {
                Name = $"Send Scenario {suffix}",
                SpecificationId = specificationId,
                MockEndpointId = mockEndpointId,
                ConnectionId = connectionId,
                PayloadOverride = """{"orderId":"ord_1"}""",
                Kind = "Send",
                Exchange = exchange
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!["id"]!.GetValue<Guid>();
    }
}
