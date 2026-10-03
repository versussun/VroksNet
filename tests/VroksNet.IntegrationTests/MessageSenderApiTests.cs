using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using NATS.Client.Core;
using RabbitMQ.Client;
using VroksNet.IntegrationTests.Fixtures;

namespace VroksNet.IntegrationTests;

/// <summary>
/// Exercises MessageSender's RabbitMq/Nats success paths end-to-end against the real broker
/// containers AppHost boots — closing the gap called out in .claude/CLAUDE.md ("RabbitMq/Nats
/// success paths aren't covered end-to-end anywhere yet"). Drives it the same way a user would —
/// import an AsyncAPI spec, create a Connection pointed at the real broker, save a TestScenario,
/// run it over HTTP — then proves the message actually arrived by consuming it directly off the
/// broker with RabbitMQ.Client/NATS.Client.Core, the same libraries MessageSender itself uses to
/// publish. Each test uses a GUID-suffixed channel address/queue/subject so repeated runs (and
/// the persisted SQLite file/Connection.Name unique index — see .claude/CLAUDE.md "Testing") don't
/// collide.
/// </summary>
[Collection("AppHost")]
public sealed class MessageSenderApiTests(AppHostFixture fixture)
{
    [Fact]
    public async Task RunTestScenario_RabbitMq_PublishesToRoutingKey()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = fixture.ApiServiceClient;
        var suffix = Guid.NewGuid();
        var channelAddress = $"orders.created.{suffix:N}";

        var rabbitMqConnectionString = await fixture.App.GetConnectionStringAsync("rabbitmq", cancellationToken);
        Assert.False(string.IsNullOrWhiteSpace(rabbitMqConnectionString));

        // MessageSender publishes to the default exchange with routingKey = channel address (see
        // its doc comment); a publish with no matching queue is silently dropped, so the queue
        // must exist before the scenario runs. exclusive:true (rather than the classic
        // non-durable/non-exclusive combo) — this RabbitMQ version has deprecated and now refuses
        // "transient_nonexcl_queues" — and it's only ever read back on this same connection anyway.
        var factory = new ConnectionFactory { Uri = new Uri(rabbitMqConnectionString!) };
        await using var rabbitConnection = await factory.CreateConnectionAsync(cancellationToken);
        await using var channel = await rabbitConnection.CreateChannelAsync(cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(channelAddress, durable: false, exclusive: true, autoDelete: true, cancellationToken: cancellationToken);

        var (specificationId, endpointId) = await ImportAsyncApiSpecAsync(client, suffix, channelAddress, cancellationToken);
        var connectionId = await CreateConnectionAsync(client, suffix, "RabbitMq", rabbitMqConnectionString!, cancellationToken);
        var scenarioId = await CreateScenarioAsync(client, suffix, specificationId, endpointId, connectionId, cancellationToken);

        var runResponse = await client.PostAsync($"/api/test-scenarios/{scenarioId}/run", null, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, runResponse.StatusCode);
        var runResult = await runResponse.Content.ReadFromJsonAsync<JsonNode>(cancellationToken);
        Assert.True(runResult!["success"]!.GetValue<bool>());

        var delivery = await channel.BasicGetAsync(channelAddress, autoAck: true, cancellationToken);
        Assert.NotNull(delivery);
        var body = Encoding.UTF8.GetString(delivery!.Body.ToArray());
        Assert.Contains("ord_1", body);
    }

    [Fact]
    public async Task RunTestScenario_Nats_PublishesToSubject()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = fixture.ApiServiceClient;
        var suffix = Guid.NewGuid();
        var subject = $"orders.created.{suffix:N}";

        var natsConnectionString = await fixture.App.GetConnectionStringAsync("nats", cancellationToken);
        Assert.False(string.IsNullOrWhiteSpace(natsConnectionString));

        // NATS core pub/sub has no queueing, so the subscription must be registered *before* the
        // scenario publishes. SubscribeCoreAsync only queues the SUB; the server handles commands in
        // order, so the PONG proves it's registered. (A SubscribeAsync enumerator doesn't subscribe
        // until its first MoveNextAsync, which raced the publish and lost on slower CI runners.)
        await using var natsConnection = new NatsConnection(new NatsOpts { Url = natsConnectionString! });
        await using var subscription = await natsConnection.SubscribeCoreAsync<string>(subject, cancellationToken: cancellationToken);
        await natsConnection.PingAsync(cancellationToken);

        var (specificationId, endpointId) = await ImportAsyncApiSpecAsync(client, suffix, subject, cancellationToken);
        var connectionId = await CreateConnectionAsync(client, suffix, "Nats", natsConnectionString!, cancellationToken);
        var scenarioId = await CreateScenarioAsync(client, suffix, specificationId, endpointId, connectionId, cancellationToken);

        // The run is synchronous and publishes before it answers, and the subscription buffers what
        // it receives — so each run's own result is checked first: when it failed, its message says
        // why. Core NATS delivers at most once, and on CI a delivery was lost now and then although
        // the server had confirmed the publish (an attempt timed out with nothing received), so a
        // missed message is retried with a fresh run, up to three times, rather than failing.
        string? received = null;
        for (var attempt = 1; attempt <= 3 && received is null; attempt++)
        {
            var runResponse = await client.PostAsync($"/api/test-scenarios/{scenarioId}/run", null, cancellationToken);
            Assert.Equal(HttpStatusCode.OK, runResponse.StatusCode);
            var runResult = await runResponse.Content.ReadFromJsonAsync<JsonNode>(cancellationToken);
            Assert.True(runResult!["success"]!.GetValue<bool>(), $"Run {attempt} failed: {runResult["message"]}");

            // Times out by cancelling the read rather than abandoning it, so nothing is left pending
            // when the subscription is disposed.
            using var receiveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            receiveCts.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                received = (await subscription.Msgs.ReadAsync(receiveCts.Token)).Data;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                TestContext.Current.SendDiagnosticMessage($"Run {attempt} published, but nothing arrived on {subject} within 10s; retrying.");
            }
        }

        Assert.NotNull(received);
        Assert.Contains("ord_1", received);
    }

    /// <summary>One "send" operation on <paramref name="channelAddress"/>, with a JSON example payload — enough for AsyncApiSpecificationParser to produce a single MockEndpoint keyed "{channelAddress}:send".</summary>
    private static async Task<(Guid SpecificationId, Guid EndpointId)> ImportAsyncApiSpecAsync(HttpClient client, Guid suffix, string channelAddress, CancellationToken cancellationToken)
    {
        var yaml = $"""
            asyncapi: 3.0.0
            info:
              title: "MessageSender Fixture {suffix}"
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
                  properties:
                    orderId:
                      type: string
            """;

        using var importRequest = new HttpRequestMessage(HttpMethod.Post, "/api/specifications/asyncapi")
        {
            Content = new StringContent(yaml, Encoding.UTF8, "text/plain"),
        };
        importRequest.Headers.Add("X-File-Name", "orders.yaml");
        var importResponse = await client.SendAsync(importRequest, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, importResponse.StatusCode);
        var imported = await importResponse.Content.ReadFromJsonAsync<JsonNode>(cancellationToken);
        var specificationId = imported!["id"]!.GetValue<Guid>();

        var detailResponse = await client.GetAsync($"/api/specifications/{specificationId}", cancellationToken);
        var details = await detailResponse.Content.ReadFromJsonAsync<JsonNode>(cancellationToken);
        var endpointId = details!["endpoints"]!.AsArray().Single()!["id"]!.GetValue<Guid>();

        return (specificationId, endpointId);
    }

    private static async Task<Guid> CreateConnectionAsync(HttpClient client, Guid suffix, string serviceType, string value, CancellationToken cancellationToken)
    {
        var response = await client.PostAsJsonAsync(
            "/api/connections",
            new { Name = $"MessageSender Test Connection {serviceType} {suffix}", ServiceType = serviceType, Value = value },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken);
        return created!["id"]!.GetValue<Guid>();
    }

    private static async Task<Guid> CreateScenarioAsync(HttpClient client, Guid suffix, Guid specificationId, Guid endpointId, Guid connectionId, CancellationToken cancellationToken)
    {
        var response = await client.PostAsJsonAsync(
            "/api/test-scenarios",
            new { Name = $"MessageSender Test Scenario {suffix}", SpecificationId = specificationId, MockEndpointId = endpointId, ConnectionId = connectionId, PayloadOverride = (string?)null },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken);
        return created!["id"]!.GetValue<Guid>();
    }
}
