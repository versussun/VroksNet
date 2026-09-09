using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using VroksNet.IntegrationTests.Fixtures;

namespace VroksNet.IntegrationTests;

/// <summary>
/// Drives the real create → list → run → update → delete flow for /api/test-scenarios over HTTP
/// against the booted ApiService — a black-box test of the wire contract, so it talks JSON rather
/// than referencing Application/Domain types directly (see .claude/CLAUDE.md "Testing"). The
/// fixture spec's one operation is deliberately "GET /health" — not a real Petstore-shaped
/// route — so running the scenario hits ApiService's own real /health endpoint (guaranteed 200),
/// the same self-referential trick ConnectionsApiTests uses for its Http reachability case.
/// </summary>
[Collection("AppHost")]
public sealed class TestScenariosApiTests(AppHostFixture fixture)
{
    [Fact]
    public async Task CreateListRunUpdateDelete_RoundTrips()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = fixture.ApiServiceClient;
        var suffix = Guid.NewGuid();

        var (specificationId, endpointId) = await ImportSpecAsync(client, suffix, cancellationToken);
        var connectionId = await CreateConnectionAsync(client, suffix, client.BaseAddress!.ToString(), cancellationToken);

        // Create
        var name = $"Integration Test Scenario {suffix}";
        var createResponse = await client.PostAsJsonAsync(
            "/api/test-scenarios",
            new { Name = name, SpecificationId = specificationId, MockEndpointId = endpointId, ConnectionId = connectionId, PayloadOverride = (string?)null },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonNode>(cancellationToken);
        var scenarioId = created!["id"]!.GetValue<Guid>();

        // List reflects it, denormalized
        var listResponse = await client.GetAsync("/api/test-scenarios", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var scenarios = await listResponse.Content.ReadFromJsonAsync<JsonArray>(cancellationToken);
        var summary = scenarios!.Single(s => s!["id"]!.GetValue<Guid>() == scenarioId);
        Assert.Equal("GET /health", summary!["operationKey"]!.GetValue<string>());
        Assert.Equal("Http", summary["connectionServiceType"]!.GetValue<string>());

        // Run — hits ApiService's own real /health endpoint
        var runResponse = await client.PostAsync($"/api/test-scenarios/{scenarioId}/run", null, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, runResponse.StatusCode);
        var runResult = await runResponse.Content.ReadFromJsonAsync<JsonNode>(cancellationToken);
        Assert.True(runResult!["success"]!.GetValue<bool>());

        // Running an unknown id 404s
        var runUnknownResponse = await client.PostAsync($"/api/test-scenarios/{Guid.NewGuid()}/run", null, cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, runUnknownResponse.StatusCode);

        // Update
        var updateResponse = await client.PutAsJsonAsync(
            $"/api/test-scenarios/{scenarioId}",
            new { Id = scenarioId, Name = name + " (renamed)", SpecificationId = specificationId, MockEndpointId = endpointId, ConnectionId = connectionId, PayloadOverride = "{}" },
            cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, updateResponse.StatusCode);

        // Updating with a mismatched operation/connection type 500s (ArgumentException — same
        // validation-error convention as CreateConnectionHandler's blank-name check; see
        // .claude/CLAUDE.md).
        var mismatchedConnectionId = await CreateConnectionAsync(client, Guid.NewGuid(), "amqp://localhost", cancellationToken, serviceType: "RabbitMq");
        var mismatchedUpdateResponse = await client.PutAsJsonAsync(
            $"/api/test-scenarios/{scenarioId}",
            new { Id = scenarioId, Name = name, SpecificationId = specificationId, MockEndpointId = endpointId, ConnectionId = mismatchedConnectionId, PayloadOverride = (string?)null },
            cancellationToken);
        Assert.Equal(HttpStatusCode.InternalServerError, mismatchedUpdateResponse.StatusCode);

        // Delete
        var deleteResponse = await client.DeleteAsync($"/api/test-scenarios/{scenarioId}", cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var deleteAgainResponse = await client.DeleteAsync($"/api/test-scenarios/{scenarioId}", cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, deleteAgainResponse.StatusCode);
    }

    private static async Task<(Guid SpecificationId, Guid EndpointId)> ImportSpecAsync(HttpClient client, Guid suffix, CancellationToken cancellationToken)
    {
        var yaml = $"""
            openapi: 3.0.3
            info:
              title: "TestScenarios Fixture {suffix}"
              version: "1.0.0"
            paths:
              /health:
                get:
                  operationId: checkHealth
                  responses:
                    "200":
                      description: OK
            """;

        using var importRequest = new HttpRequestMessage(HttpMethod.Post, "/api/specifications/openapi")
        {
            Content = new StringContent(yaml, Encoding.UTF8, "text/plain"),
        };
        importRequest.Headers.Add("X-File-Name", "health.yaml");
        var importResponse = await client.SendAsync(importRequest, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, importResponse.StatusCode);
        var imported = await importResponse.Content.ReadFromJsonAsync<JsonNode>(cancellationToken);
        var specificationId = imported!["id"]!.GetValue<Guid>();

        var detailResponse = await client.GetAsync($"/api/specifications/{specificationId}", cancellationToken);
        var details = await detailResponse.Content.ReadFromJsonAsync<JsonNode>(cancellationToken);
        var endpointId = details!["endpoints"]!.AsArray().Single()!["id"]!.GetValue<Guid>();

        return (specificationId, endpointId);
    }

    private static async Task<Guid> CreateConnectionAsync(HttpClient client, Guid suffix, string value, CancellationToken cancellationToken, string serviceType = "Http")
    {
        var response = await client.PostAsJsonAsync(
            "/api/connections",
            new { Name = $"Integration Test Connection {suffix}", ServiceType = serviceType, Value = value },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken);
        return created!["id"]!.GetValue<Guid>();
    }
}
