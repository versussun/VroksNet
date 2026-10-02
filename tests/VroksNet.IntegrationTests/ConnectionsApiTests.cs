using System.Net.Http.Json;
using System.Text.Json.Nodes;
using VroksNet.IntegrationTests.Fixtures;

namespace VroksNet.IntegrationTests;

/// <summary>
/// Drives the real /api/connections endpoints over HTTP against the booted ApiService — a
/// black-box test of the wire contract, so it talks JSON rather than referencing Application/
/// Domain types directly (VroksNet.IntegrationTests has no project reference to them; see .claude/CLAUDE.md
/// "Testing"). Each test uses a GUID-suffixed name — Connection.Name has a unique index (see
/// .claude/CLAUDE.md "Infrastructure notes") and the SQLite file persists across test runs.
/// </summary>
[Collection("AppHost")]
public sealed class ConnectionsApiTests(AppHostFixture fixture)
{
    [Fact]
    public async Task CreateListUpdateDelete_RoundTrips()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = fixture.ApiServiceClient;
        var name = $"Integration Test Connection {Guid.NewGuid()}";

        // Create
        var createResponse = await client.PostAsJsonAsync(
            "/api/connections",
            new { Name = name, ServiceType = "Http", Value = "https://api.example.com" },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonNode>(cancellationToken);
        var id = created!["id"]!.GetValue<Guid>();

        // List reflects the new connection
        var afterCreate = await GetConnectionAsync(client, id, cancellationToken);
        Assert.NotNull(afterCreate);
        Assert.Equal(name, afterCreate!["name"]!.GetValue<string>());
        Assert.Equal("Http", afterCreate["serviceType"]!.GetValue<string>());

        // Update
        var updateResponse = await client.PutAsJsonAsync(
            $"/api/connections/{id}",
            new { Id = id, Name = name, ServiceType = "RabbitMq", Value = "amqp://localhost" },
            cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, updateResponse.StatusCode);

        var afterUpdate = await GetConnectionAsync(client, id, cancellationToken);
        Assert.NotNull(afterUpdate);
        Assert.Equal("RabbitMq", afterUpdate!["serviceType"]!.GetValue<string>());
        Assert.Equal("amqp://localhost", afterUpdate["value"]!.GetValue<string>());

        // Updating an unknown id 404s
        var updateUnknownResponse = await client.PutAsJsonAsync(
            $"/api/connections/{Guid.NewGuid()}",
            new { Id = Guid.NewGuid(), Name = name, ServiceType = "Http", Value = "https://x" },
            cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, updateUnknownResponse.StatusCode);

        // Delete
        var deleteResponse = await client.DeleteAsync($"/api/connections/{id}", cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        Assert.Null(await GetConnectionAsync(client, id, cancellationToken));

        // Deleting again 404s
        var deleteAgainResponse = await client.DeleteAsync($"/api/connections/{id}", cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, deleteAgainResponse.StatusCode);
    }

    [Fact]
    public async Task TestConnection_Http_ReportsReachableAndUnreachable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = fixture.ApiServiceClient;

        // Reachable: point it back at the booted ApiService's own health endpoint.
        var reachableId = await CreateConnectionAsync(client, "Http", new Uri(fixture.ApiServiceHttpAddress, "/health").ToString(), cancellationToken);
        var reachableResult = await TestConnectionAsync(client, reachableId, cancellationToken);
        Assert.True(reachableResult!["success"]!.GetValue<bool>());

        // Unreachable: ".invalid" is reserved by RFC 2606 to never resolve.
        var unreachableId = await CreateConnectionAsync(client, "Http", "https://this-host-does-not-exist.invalid", cancellationToken);
        var unreachableResult = await TestConnectionAsync(client, unreachableId, cancellationToken);
        Assert.False(unreachableResult!["success"]!.GetValue<bool>());
        Assert.False(string.IsNullOrWhiteSpace(unreachableResult["message"]!.GetValue<string>()));

        // Testing an unknown id 404s.
        var testUnknownResponse = await client.PostAsync($"/api/connections/{Guid.NewGuid()}/test", null, cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, testUnknownResponse.StatusCode);
    }

    private static async Task<Guid> CreateConnectionAsync(HttpClient client, string serviceType, string value, CancellationToken cancellationToken)
    {
        var response = await client.PostAsJsonAsync(
            "/api/connections",
            new { Name = $"Integration Test Connection {Guid.NewGuid()}", ServiceType = serviceType, Value = value },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken);
        return created!["id"]!.GetValue<Guid>();
    }

    private static async Task<JsonNode?> TestConnectionAsync(HttpClient client, Guid id, CancellationToken cancellationToken)
    {
        var response = await client.PostAsync($"/api/connections/{id}/test", null, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken);
    }

    private static async Task<JsonNode?> GetConnectionAsync(HttpClient client, Guid id, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync("/api/connections", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var connections = await response.Content.ReadFromJsonAsync<JsonArray>(cancellationToken);
        return connections!.SingleOrDefault(connection => connection!["id"]!.GetValue<Guid>() == id);
    }
}
