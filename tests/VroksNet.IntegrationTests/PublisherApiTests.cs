using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using NATS.Client.Core;
using RabbitMQ.Client;
using VroksNet.IntegrationTests.Fixtures;

namespace VroksNet.IntegrationTests;

/// <summary>Publishers (async mocks, Фаза 03) against the real RabbitMQ/NATS containers AppHost boots: on a schedule and on demand.</summary>
[Collection("AppHost")]
public sealed class PublisherApiTests(AppHostFixture fixture)
{
    [Fact]
    public async Task EnabledPublisher_PublishesOnItsSchedule_WithFreshPlaceholders_UntilStopped()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = fixture.ApiServiceClient;
        var suffix = Guid.NewGuid().ToString("N");
        var subject = $"publisher.{suffix}.created";

        var connectionString = await fixture.App.GetConnectionStringAsync("nats", cancellationToken);
        Assert.NotNull(connectionString);
        var (specificationId, endpointId) = await ImportAsync(client, suffix, subject, cancellationToken);
        var connectionId = await CreateConnectionAsync(client, suffix, "Nats", connectionString, cancellationToken);

        await using var nats = new NatsConnection(new NatsOpts { Url = connectionString });
        await using var subscription = await nats.SubscribeCoreAsync<string>(subject, cancellationToken: cancellationToken);
        await nats.PingAsync(cancellationToken);

        var publisherId = await CreatePublisherAsync(client, new
        {
            Name = $"Scheduled {suffix}",
            SpecificationId = specificationId,
            MockEndpointId = endpointId,
            ConnectionId = connectionId,
            PayloadOverride = """{"orderId":"{{uuid}}"}""",
            IntervalSeconds = 1,
            Enabled = true
        }, cancellationToken);

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            var orderIds = new List<string>();
            await foreach (var message in subscription.Msgs.ReadAllAsync(timeout.Token))
            {
                orderIds.Add(JsonNode.Parse(message.Data!)!["orderId"]!.GetValue<string>());
                if (orderIds.Count == 2)
                {
                    break;
                }
            }

            Assert.All(orderIds, id => Assert.True(Guid.TryParse(id, out _)));
            Assert.NotEqual(orderIds[0], orderIds[1]); // {{uuid}} is rendered per message
        }
        finally
        {
            var stop = await client.PutAsJsonAsync($"/api/publishers/{publisherId}/enabled", new { enabled = false }, cancellationToken);
            Assert.Equal(HttpStatusCode.NoContent, stop.StatusCode);
        }

        var publishers = await client.GetFromJsonAsync<JsonNode>("/api/publishers", cancellationToken);
        var listed = publishers!.AsArray().Single(p => p!["id"]!.GetValue<Guid>() == publisherId)!;
        Assert.False(listed["isEnabled"]!.GetValue<bool>());
        Assert.True(listed["lastPublishSuccess"]!.GetValue<bool>());

        var history = await client.GetFromJsonAsync<JsonNode>($"/api/call-records?publisherId={publisherId}", cancellationToken);
        var record = history!["items"]![0]!;
        Assert.Equal("OutboundBrokerPublish", record["direction"]!.GetValue<string>());
        Assert.Equal($"Scheduled {suffix}", record["publisherName"]!.GetValue<string>());
        Assert.True(record["contractValid"]!.GetValue<bool>());
    }

    [Fact]
    public async Task PublishNow_RabbitMq_DeliversToTheExchange()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = fixture.ApiServiceClient;
        var suffix = Guid.NewGuid().ToString("N");
        var routingKey = $"publisher.{suffix}.created";

        var connectionString = await fixture.App.GetConnectionStringAsync("rabbitmq", cancellationToken);
        Assert.NotNull(connectionString);
        var (specificationId, endpointId) = await ImportAsync(client, suffix, routingKey, cancellationToken);
        var connectionId = await CreateConnectionAsync(client, suffix, "RabbitMq", connectionString, cancellationToken);

        var factory = new ConnectionFactory { Uri = new Uri(connectionString) };
        await using var rabbit = await factory.CreateConnectionAsync(cancellationToken);
        await using var channel = await rabbit.CreateChannelAsync(cancellationToken: cancellationToken);
        var queue = await channel.QueueDeclareAsync(string.Empty, durable: false, exclusive: true, autoDelete: true, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(queue.QueueName, "amq.topic", routingKey, cancellationToken: cancellationToken);

        var publisherId = await CreatePublisherAsync(client, new
        {
            Name = $"On demand {suffix}",
            SpecificationId = specificationId,
            MockEndpointId = endpointId,
            ConnectionId = connectionId,
            PayloadOverride = (string?)null,
            IntervalSeconds = 3600,
            Exchange = "amq.topic",
            Enabled = false
        }, cancellationToken);

        var response = await client.PostAsync($"/api/publishers/{publisherId}/publish", null, cancellationToken);
        var result = (await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!;
        Assert.True(result["success"]!.GetValue<bool>(), result["message"]?.GetValue<string>());

        var delivered = await channel.BasicGetAsync(queue.QueueName, autoAck: true, cancellationToken);
        Assert.NotNull(delivered);
        Assert.Equal(result["payload"]!.GetValue<string>(), Encoding.UTF8.GetString(delivered.Body.Span));
        Assert.Contains("ord_1", result["payload"]!.GetValue<string>()); // the spec's own example

        await client.DeleteAsync($"/api/publishers/{publisherId}", cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/publishers/{publisherId}/publish", null, cancellationToken)).StatusCode);
    }

    [Fact]
    public async Task InvalidPublisher_Is400WithTheReason()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = fixture.ApiServiceClient;
        var suffix = Guid.NewGuid().ToString("N");

        var connectionString = await fixture.App.GetConnectionStringAsync("nats", cancellationToken);
        Assert.NotNull(connectionString);
        var (specificationId, endpointId) = await ImportAsync(client, suffix, $"publisher.{suffix}.bad", cancellationToken);
        var connectionId = await CreateConnectionAsync(client, suffix, "Nats", connectionString, cancellationToken);

        var response = await client.PostAsJsonAsync("/api/publishers", new
        {
            Name = "Too fast",
            SpecificationId = specificationId,
            MockEndpointId = endpointId,
            ConnectionId = connectionId,
            IntervalSeconds = 0
        }, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken);
        Assert.Contains("interval", problem!["detail"]!.GetValue<string>());
    }

    private static async Task<Guid> CreatePublisherAsync(HttpClient client, object body, CancellationToken cancellationToken)
    {
        var response = await client.PostAsJsonAsync("/api/publishers", body, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!["id"]!.GetValue<Guid>();
    }

    private static async Task<Guid> CreateConnectionAsync(HttpClient client, string suffix, string serviceType, string value, CancellationToken cancellationToken)
    {
        var response = await client.PostAsJsonAsync(
            "/api/connections",
            new { Name = $"Publisher Connection {serviceType} {suffix}", ServiceType = serviceType, Value = value },
            cancellationToken);
        return (await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!["id"]!.GetValue<Guid>();
    }

    /// <summary>An AsyncAPI spec with one operation on <paramref name="channelAddress"/>, whose example is <c>{"orderId":"ord_1"}</c>.</summary>
    private static async Task<(Guid SpecificationId, Guid EndpointId)> ImportAsync(HttpClient client, string suffix, string channelAddress, CancellationToken cancellationToken)
    {
        var yaml = $"""
            asyncapi: 3.0.0
            info:
              title: "Publisher Fixture {suffix}"
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
                    type: object
                    required: [orderId]
                    properties:
                      orderId:
                        type: string
                  examples:
                    - payload:
                        orderId: ord_1
            """;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/specifications/asyncapi")
        {
            Content = new StringContent(yaml, Encoding.UTF8, "text/plain"),
        };
        request.Headers.Add("X-File-Name", "publisher.yaml");
        var response = await client.SendAsync(request, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var specificationId = (await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!["id"]!.GetValue<Guid>();

        var details = await client.GetFromJsonAsync<JsonNode>($"/api/specifications/{specificationId}", cancellationToken);
        var endpoint = details!["endpoints"]!.AsArray().Single()!;
        Assert.Contains("ord_1", endpoint["exampleTemplate"]!.GetValue<string>());
        return (specificationId, endpoint["id"]!.GetValue<Guid>());
    }
}
