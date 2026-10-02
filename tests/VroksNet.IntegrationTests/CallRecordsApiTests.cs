using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using VroksNet.IntegrationTests.Fixtures;

namespace VroksNet.IntegrationTests;

/// <summary>
/// The call history over the wire: inbound mock calls (matched and unmatched) and Test Scenario
/// runs land in GET /api/call-records, filters and paging work as query parameters, and DELETE
/// clears it. Each test narrows by its own GUID-suffixed spec/path/scenario, since other tests in
/// the collection log calls too.
/// </summary>
[Collection("AppHost")]
public sealed class CallRecordsApiTests(AppHostFixture fixture)
{
    [Fact]
    public async Task MockCalls_AreLoggedNewestFirst_AndPageWithACursor()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = fixture.ApiServiceClient;
        var suffix = Guid.NewGuid().ToString("N");
        var specificationId = await ImportSpecAsync(client, suffix, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/mock/history-{suffix}/pets?first=1", cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/mock/history-{suffix}/pets?second=1", cancellationToken)).StatusCode);

        var firstPage = await GetPageAsync(client, $"specificationId={specificationId}&limit=1", cancellationToken);
        var newest = Assert.Single(firstPage["items"]!.AsArray())!;
        Assert.Equal("InboundHttpRequest", newest["direction"]!.GetValue<string>());
        Assert.Equal($"GET /mock/history-{suffix}/pets?second=1", newest["requestLine"]!.GetValue<string>());
        Assert.Equal($"GET /history-{suffix}/pets", newest["operationKey"]!.GetValue<string>());
        Assert.Equal(200, newest["statusCode"]!.GetValue<int>());
        Assert.Null(newest["responseSnapshot"]); // bodies aren't in the list…

        // …they're fetched per record.
        var details = await client.GetFromJsonAsync<JsonNode>($"/api/call-records/{newest["id"]!.GetValue<Guid>()}", cancellationToken);
        Assert.Contains("Fido", details!["responseSnapshot"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/call-records/{Guid.NewGuid()}", cancellationToken)).StatusCode);

        var cursor = firstPage["nextCursor"]!.GetValue<string>();
        var secondPage = await GetPageAsync(client, $"specificationId={specificationId}&limit=1&cursor={Uri.EscapeDataString(cursor)}", cancellationToken);
        var older = Assert.Single(secondPage["items"]!.AsArray())!;
        Assert.Equal($"GET /mock/history-{suffix}/pets?first=1", older["requestLine"]!.GetValue<string>());
        Assert.Null(secondPage["nextCursor"]);
    }

    [Fact]
    public async Task UnmatchedMockCall_IsLoggedAs404WithItsRequestLine()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = fixture.ApiServiceClient;
        var path = $"/mock/nowhere-{Guid.NewGuid():N}";

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path, cancellationToken)).StatusCode);

        var page = await GetPageAsync(client, "direction=InboundHttpRequest&limit=200", cancellationToken);
        var logged = page["items"]!.AsArray().Single(item => item!["requestLine"]?.GetValue<string>() == $"GET {path}")!;
        Assert.Equal(404, logged["statusCode"]!.GetValue<int>());
        Assert.Null(logged["specificationId"]);
    }

    [Fact]
    public async Task TestScenarioRun_IsLoggedAndFilterableByScenario()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = fixture.ApiServiceClient;
        var suffix = Guid.NewGuid().ToString("N");
        var specificationId = await ImportSpecAsync(client, suffix, cancellationToken);
        var endpointId = await GetSingleEndpointIdAsync(client, specificationId, cancellationToken);

        // Sent to ApiService's own mock, so one run also produces an inbound record.
        var connectionResponse = await client.PostAsJsonAsync(
            "/api/connections",
            new { Name = $"History Connection {suffix}", ServiceType = "Http", Value = new Uri(fixture.ApiServiceHttpAddress, "/mock").ToString() + "/" },
            cancellationToken);
        var connectionId = (await connectionResponse.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!["id"]!.GetValue<Guid>();
        var scenarioResponse = await client.PostAsJsonAsync(
            "/api/test-scenarios",
            new { Name = $"History Scenario {suffix}", SpecificationId = specificationId, MockEndpointId = endpointId, ConnectionId = connectionId, PayloadOverride = (string?)null },
            cancellationToken);
        var scenarioId = (await scenarioResponse.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!["id"]!.GetValue<Guid>();

        var runResponse = await client.PostAsync($"/api/test-scenarios/{scenarioId}/run", null, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, runResponse.StatusCode);

        var page = await GetPageAsync(client, $"testScenarioId={scenarioId}", cancellationToken);
        var run = Assert.Single(page["items"]!.AsArray())!;
        Assert.Equal("OutboundHttpRequest", run["direction"]!.GetValue<string>());
        Assert.Equal($"History Scenario {suffix}", run["testScenarioName"]!.GetValue<string>());
        Assert.Equal($"History Connection {suffix}", run["connectionName"]!.GetValue<string>());
        Assert.Equal(200, run["statusCode"]!.GetValue<int>());
        Assert.True(run["contractValid"]!.GetValue<bool>());

        var inbound = await GetPageAsync(client, $"specificationId={specificationId}&direction=InboundHttpRequest", cancellationToken);
        Assert.Single(inbound["items"]!.AsArray());
    }

    [Fact]
    public async Task Delete_ClearsTheWholeHistory()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = fixture.ApiServiceClient;
        await client.GetAsync($"/mock/nowhere-{Guid.NewGuid():N}", cancellationToken);

        var deleteResponse = await client.DeleteAsync("/api/call-records", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);
        var cleared = await deleteResponse.Content.ReadFromJsonAsync<JsonNode>(cancellationToken);
        Assert.True(cleared!["deleted"]!.GetValue<int>() >= 1);

        var page = await GetPageAsync(client, string.Empty, cancellationToken);
        Assert.Empty(page["items"]!.AsArray());
    }

    private static async Task<JsonNode> GetPageAsync(HttpClient client, string query, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync($"/api/call-records?{query}", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!;
    }

    /// <summary>One "GET /history-{suffix}/pets" operation declaring a JSON array, with an example — unique per test so its records can be told apart.</summary>
    private static async Task<Guid> ImportSpecAsync(HttpClient client, string suffix, CancellationToken cancellationToken)
    {
        var yaml = $"""
            openapi: 3.0.3
            info:
              title: "History Fixture {suffix}"
              version: "1.0.0"
            paths:
              /history-{suffix}/pets:
                get:
                  responses:
                    "200":
                      description: OK
                      content:
                        application/json:
                          schema:
                            type: array
                          example:
                            - name: Fido
            """;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/specifications/openapi")
        {
            Content = new StringContent(yaml, Encoding.UTF8, "text/plain"),
        };
        request.Headers.Add("X-File-Name", "history.yaml");
        var response = await client.SendAsync(request, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!["id"]!.GetValue<Guid>();
    }

    private static async Task<Guid> GetSingleEndpointIdAsync(HttpClient client, Guid specificationId, CancellationToken cancellationToken)
    {
        var details = await client.GetFromJsonAsync<JsonNode>($"/api/specifications/{specificationId}", cancellationToken);
        return details!["endpoints"]!.AsArray().Single()!["id"]!.GetValue<Guid>();
    }
}
