using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using VroksNet.IntegrationTests.Fixtures;

namespace VroksNet.IntegrationTests;

/// <summary>
/// Kafka connections end-to-end against the real Kafka container AppHost boots: the connection
/// test, a Send scenario (proved by reading the topic back with Confluent.Kafka, the library
/// MessageSender itself publishes with) and Listen scenarios. Topics are created up front, so the
/// tests don't depend on the broker auto-creating them, and are GUID-suffixed so repeated runs
/// don't collide.
/// </summary>
[Collection("AppHost")]
public sealed class KafkaApiTests(AppHostFixture fixture)
{
    [Fact]
    public async Task TestConnectionValue_Kafka_Succeeds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var bootstrapServers = await BootstrapServersAsync(cancellationToken);

        var response = await fixture.ApiServiceClient.PostAsJsonAsync("/api/connections/test", new { ServiceType = "Kafka", Value = bootstrapServers }, cancellationToken);
        var result = (await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!;

        Assert.True(result["success"]!.GetValue<bool>(), result["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task RunTestScenario_Kafka_PublishesToTopic()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = fixture.ApiServiceClient;
        var suffix = Guid.NewGuid().ToString("N");
        var topic = $"orders.created.{suffix}";
        var bootstrapServers = await BootstrapServersAsync(cancellationToken);
        await CreateTopicAsync(bootstrapServers, topic);

        var scenarioId = await CreateScenarioAsync(client, suffix, topic, bootstrapServers, kind: "Send", timeoutSeconds: null, cancellationToken);
        var run = await RunAsync(scenarioId, cancellationToken);
        Assert.True(run["success"]!.GetValue<bool>(), run["message"]!.GetValue<string>());

        using var consumer = new ConsumerBuilder<Ignore, string>(new ConsumerConfig { BootstrapServers = bootstrapServers, GroupId = $"test-{suffix}" }).Build();
        consumer.Assign(new TopicPartitionOffset(topic, 0, Offset.Beginning));
        var consumed = consumer.Consume(TimeSpan.FromSeconds(10));
        Assert.NotNull(consumed);
        Assert.Contains("ord_1", consumed.Message.Value);
    }

    [Fact]
    public async Task Listen_Kafka_ReceivesOnTopicAndValidates()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var topic = $"orders.created.{suffix}";
        var bootstrapServers = await BootstrapServersAsync(cancellationToken);
        await CreateTopicAsync(bootstrapServers, topic);

        var scenarioId = await CreateScenarioAsync(fixture.ApiServiceClient, suffix, topic, bootstrapServers, kind: "Listen", timeoutSeconds: 20, cancellationToken);
        var run = await RunWhileProducingAsync(scenarioId, bootstrapServers, topic, cancellationToken);

        Assert.True(run["success"]!.GetValue<bool>(), run["message"]!.GetValue<string>());
        Assert.True(run["contractValidation"]!["isValid"]!.GetValue<bool>());

        var history = await fixture.ApiServiceClient.GetFromJsonAsync<JsonNode>($"/api/call-records?testScenarioId={scenarioId}", cancellationToken);
        Assert.Equal("InboundBrokerMessage", Assert.Single(history!["items"]!.AsArray())!["direction"]!.GetValue<string>());
    }

    [Fact]
    public async Task Listen_Kafka_ChannelParameterMatchesAnyTopicInThatSegment()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var bootstrapServers = await BootstrapServersAsync(cancellationToken);
        await CreateTopicAsync(bootstrapServers, $"listen.{suffix}.eu");

        // "{region}" is a whole "."-separated segment, so the run listens on every "listen.{suffix}.<x>" topic.
        var scenarioId = await CreateScenarioAsync(fixture.ApiServiceClient, suffix, $"listen.{suffix}.{{region}}", bootstrapServers, kind: "Listen", timeoutSeconds: 20, cancellationToken);
        var run = await RunWhileProducingAsync(scenarioId, bootstrapServers, $"listen.{suffix}.eu", cancellationToken);

        Assert.True(run["success"]!.GetValue<bool>(), run["message"]!.GetValue<string>());
        Assert.Contains($"listen.{suffix}.eu", run["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task Listen_Kafka_MissingTopic_FailsReadably()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var bootstrapServers = await BootstrapServersAsync(cancellationToken);

        var scenarioId = await CreateScenarioAsync(fixture.ApiServiceClient, suffix, $"missing.{suffix}", bootstrapServers, kind: "Listen", timeoutSeconds: 1, cancellationToken);
        var run = await RunAsync(scenarioId, cancellationToken);

        Assert.False(run["success"]!.GetValue<bool>());
        Assert.Contains($"No topic matching \"missing.{suffix}\"", run["message"]!.GetValue<string>());
    }

    private async Task<string> BootstrapServersAsync(CancellationToken cancellationToken)
    {
        var connectionString = await fixture.App.GetConnectionStringAsync("kafka", cancellationToken);
        Assert.False(string.IsNullOrWhiteSpace(connectionString));
        return connectionString;
    }

    private static async Task CreateTopicAsync(string bootstrapServers, string topic)
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = bootstrapServers }).Build();
        await admin.CreateTopicsAsync([new TopicSpecification { Name = topic, NumPartitions = 1, ReplicationFactor = 1 }]);
    }

    /// <summary>
    /// Starts the run, then produces every 250ms until it returns — so a message lands whenever
    /// the run's assignment goes live. Same reasoning as ListenScenarioApiTests: a plain HttpClient,
    /// since the fixture's resilience handler would time a long run out at 10s and retry it.
    /// </summary>
    private async Task<JsonNode> RunWhileProducingAsync(Guid scenarioId, string bootstrapServers, string topic, CancellationToken cancellationToken)
    {
        using var producer = new ProducerBuilder<Null, string>(new ProducerConfig { BootstrapServers = bootstrapServers }).Build();
        using var runClient = new HttpClient { BaseAddress = fixture.ApiServiceHttpAddress, Timeout = TimeSpan.FromMinutes(2) };
        var runTask = runClient.PostAsync($"/api/test-scenarios/{scenarioId}/run", null, cancellationToken);
        try
        {
            while (!runTask.IsCompleted)
            {
                await producer.ProduceAsync(topic, new Message<Null, string> { Value = """{"orderId":"ord_1"}""" }, cancellationToken);
                await Task.WhenAny(runTask, Task.Delay(250, cancellationToken));
            }
        }
        finally
        {
            await Task.WhenAny(runTask);
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

    /// <summary>Imports a one-operation AsyncAPI spec on <paramref name="channelAddress"/>, adds a Kafka connection and saves a scenario of <paramref name="kind"/> for it.</summary>
    private static async Task<Guid> CreateScenarioAsync(
        HttpClient client, string suffix, string channelAddress, string bootstrapServers, string kind, int? timeoutSeconds, CancellationToken cancellationToken)
    {
        var yaml = $"""
            asyncapi: 3.0.0
            info:
              title: "Kafka Fixture {suffix}"
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
                  examples:
                    - name: OrderCreatedExample
                      payload:
                        orderId: "ord_1"
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
        importRequest.Headers.Add("X-File-Name", "kafka.yaml");
        var importResponse = await client.SendAsync(importRequest, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, importResponse.StatusCode);
        var specificationId = (await importResponse.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!["id"]!.GetValue<Guid>();

        var details = await client.GetFromJsonAsync<JsonNode>($"/api/specifications/{specificationId}", cancellationToken);
        var endpointId = details!["endpoints"]!.AsArray().Single()!["id"]!.GetValue<Guid>();

        var connectionResponse = await client.PostAsJsonAsync(
            "/api/connections",
            new { Name = $"Kafka Connection {suffix}", ServiceType = "Kafka", Value = bootstrapServers },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, connectionResponse.StatusCode);
        var connectionId = (await connectionResponse.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!["id"]!.GetValue<Guid>();

        var scenarioResponse = await client.PostAsJsonAsync(
            "/api/test-scenarios",
            new
            {
                Name = $"Kafka Scenario {suffix}",
                SpecificationId = specificationId,
                MockEndpointId = endpointId,
                ConnectionId = connectionId,
                PayloadOverride = (string?)null,
                Kind = kind,
                ListenTimeoutSeconds = timeoutSeconds
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, scenarioResponse.StatusCode);
        return (await scenarioResponse.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!["id"]!.GetValue<Guid>();
    }
}
