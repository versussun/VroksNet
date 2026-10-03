using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using VroksNet.IntegrationTests.Fixtures;

namespace VroksNet.IntegrationTests;

/// <summary>
/// Connections, test scenarios and Publishers have unique names (step A2): reusing one is a 400
/// that says why — not the 500 a violated unique index would give.
/// </summary>
[Collection("AppHost")]
public sealed class UniqueNamesApiTests(AppHostFixture fixture)
{
    [Fact]
    public async Task TakenNames_AreA400WithTheReason()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = fixture.ApiServiceClient;
        var suffix = Guid.NewGuid().ToString("N");

        var (openApiId, httpEndpointId, asyncApiId, channelEndpointId) = await ImportAsync(client, suffix, cancellationToken);

        var connectionName = $"Unique connection {suffix}";
        var httpConnectionId = await PostIdAsync(client, "/api/connections", new { Name = connectionName, ServiceType = "Http", Value = fixture.ApiServiceHttpAddress.ToString() }, cancellationToken);
        var natsConnectionId = await PostIdAsync(client, "/api/connections", new { Name = $"Unique NATS {suffix}", ServiceType = "Nats", Value = "nats://localhost:4222" }, cancellationToken);
        await AssertTakenAsync(
            await client.PostAsJsonAsync("/api/connections", new { Name = $" {connectionName} ", ServiceType = "Http", Value = "https://other.example" }, cancellationToken),
            $"A connection named \"{connectionName}\" already exists.", cancellationToken);

        var scenarioName = $"Unique scenario {suffix}";
        var scenario = new { Name = scenarioName, SpecificationId = openApiId, MockEndpointId = httpEndpointId, ConnectionId = httpConnectionId };
        var scenarioId = await PostIdAsync(client, "/api/test-scenarios", scenario, cancellationToken);
        await AssertTakenAsync(await client.PostAsJsonAsync("/api/test-scenarios", scenario, cancellationToken), $"A test scenario named \"{scenarioName}\" already exists.", cancellationToken);
        // Saving a scenario under its own name is fine.
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/api/test-scenarios/{scenarioId}", scenario, cancellationToken)).StatusCode);

        var publisherName = $"Unique publisher {suffix}";
        var publisher = new { Name = publisherName, SpecificationId = asyncApiId, MockEndpointId = channelEndpointId, ConnectionId = natsConnectionId, IntervalSeconds = 60 };
        await PostIdAsync(client, "/api/publishers", publisher, cancellationToken);
        await AssertTakenAsync(await client.PostAsJsonAsync("/api/publishers", publisher, cancellationToken), $"A publisher named \"{publisherName}\" already exists.", cancellationToken);
    }

    private static async Task AssertTakenAsync(HttpResponseMessage response, string expectedDetail, CancellationToken cancellationToken)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken);
        Assert.Equal(expectedDetail, problem!["detail"]!.GetValue<string>());
    }

    private static async Task<Guid> PostIdAsync(HttpClient client, string path, object body, CancellationToken cancellationToken)
    {
        var response = await client.PostAsJsonAsync(path, body, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!["id"]!.GetValue<Guid>();
    }

    /// <summary>An OpenAPI spec for the scenario and an AsyncAPI one for the publisher.</summary>
    private static async Task<(Guid OpenApiId, Guid HttpEndpointId, Guid AsyncApiId, Guid ChannelEndpointId)> ImportAsync(HttpClient client, string suffix, CancellationToken cancellationToken)
    {
        var openApiId = await ImportYamlAsync(client, "openapi", $"""
            openapi: 3.0.3
            info: {"{"} title: "Unique names {suffix}", version: "1" {"}"}
            paths:
              /health:
                get:
                  responses:
                    "200": {"{"} description: OK {"}"}
            """, cancellationToken);
        var asyncApiId = await ImportYamlAsync(client, "asyncapi", $"""
            asyncapi: 3.0.0
            info: {"{"} title: "Unique names events {suffix}", version: "1" {"}"}
            channels:
              created:
                address: unique.{suffix}
                messages:
                  created:
                    payload: {"{"} type: object {"}"}
            operations:
              publishCreated:
                action: send
                channel: {"{"} $ref: "#/channels/created" {"}"}
                messages:
                  - $ref: "#/channels/created/messages/created"
            """, cancellationToken);

        return (
            openApiId, await SingleEndpointIdAsync(client, openApiId, cancellationToken),
            asyncApiId, await SingleEndpointIdAsync(client, asyncApiId, cancellationToken));
    }

    private static async Task<Guid> ImportYamlAsync(HttpClient client, string kind, string yaml, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/specifications/{kind}") { Content = new StringContent(yaml, Encoding.UTF8, "text/plain") };
        var response = await client.SendAsync(request, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!["id"]!.GetValue<Guid>();
    }

    private static async Task<Guid> SingleEndpointIdAsync(HttpClient client, Guid specificationId, CancellationToken cancellationToken)
    {
        var details = await client.GetFromJsonAsync<JsonNode>($"/api/specifications/{specificationId}", cancellationToken);
        return details!["endpoints"]!.AsArray().Single()!["id"]!.GetValue<Guid>();
    }
}
