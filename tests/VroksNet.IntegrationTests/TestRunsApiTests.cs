using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using VroksNet.IntegrationTests.Fixtures;

namespace VroksNet.IntegrationTests;

/// <summary>
/// Background test runs and the run history (ADR 0002) end-to-end: POST /runs queues a run, the
/// real TestRunBackgroundService executes it, and /api/test-runs reports it. The HTTP scenario hits
/// ApiService's own /health (the trick TestScenariosApiTests uses); the Listen one waits on the
/// AppHost's NATS container for a message that never comes, so it can be cancelled mid-run.
/// </summary>
[Collection("AppHost")]
public sealed class TestRunsApiTests(AppHostFixture fixture)
{
    [Fact]
    public async Task BackgroundRun_IsExecutedByTheWorker_AndShowsUpInTheHistoryWithItsTraffic()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = fixture.ApiServiceClient;
        var suffix = Guid.NewGuid();
        var scenarioId = await CreateHttpScenarioAsync(client, suffix, cancellationToken);

        var start = await client.PostAsync($"/api/test-scenarios/{scenarioId}/runs", null, cancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        var runId = (await start.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!["runId"]!.GetValue<Guid>();
        Assert.Equal($"/api/test-runs/{runId}", start.Headers.Location!.OriginalString);

        var run = await WaitForFinalStatusAsync(client, runId, cancellationToken);
        Assert.Equal("Passed", run["status"]!.GetValue<string>());
        Assert.Equal("Manual", run["trigger"]!.GetValue<string>());
        Assert.Equal(200, run["statusCode"]!.GetValue<int>());

        // A synchronous run is recorded in the same history.
        var syncRun = await client.PostAsync($"/api/test-scenarios/{scenarioId}/run", null, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, syncRun.StatusCode);
        var history = await client.GetFromJsonAsync<JsonNode>($"/api/test-runs?testScenarioId={scenarioId}", cancellationToken);
        var items = history!["items"]!.AsArray();
        Assert.Equal(2, items.Count);
        Assert.All(items, item => Assert.Equal("Passed", item!["status"]!.GetValue<string>()));
        Assert.Equal(runId, items[1]!["id"]!.GetValue<Guid>()); // newest first: the synchronous run came later

        // The run's traffic, through the call-history filter.
        var traffic = await client.GetFromJsonAsync<JsonNode>($"/api/call-records?testRunId={runId}", cancellationToken);
        Assert.Single(traffic!["items"]!.AsArray());
    }

    [Fact]
    public async Task LongListen_RunsOnlyInTheBackground_AndCanBeCancelledWhileRunning()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = fixture.ApiServiceClient;
        var suffix = Guid.NewGuid();
        var scenarioId = await CreateListenScenarioAsync(client, suffix, timeoutSeconds: 600, cancellationToken);

        // Too long to run synchronously: refused, nothing recorded.
        var sync = (await (await client.PostAsync($"/api/test-scenarios/{scenarioId}/run", null, cancellationToken))
            .Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!;
        Assert.False(sync["success"]!.GetValue<bool>());
        Assert.Contains("Run it in the background", sync["message"]!.GetValue<string>());

        var start = await client.PostAsync($"/api/test-scenarios/{scenarioId}/runs", null, cancellationToken);
        var runId = (await start.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!["runId"]!.GetValue<Guid>();
        await WaitForStatusAsync(client, runId, "Running", cancellationToken);

        var cancel = await client.PostAsync($"/api/test-runs/{runId}/cancel", null, cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);

        var run = await WaitForFinalStatusAsync(client, runId, cancellationToken);
        Assert.Equal("Cancelled", run["status"]!.GetValue<string>());

        var cancelAgain = await client.PostAsync($"/api/test-runs/{runId}/cancel", null, cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, cancelAgain.StatusCode);
    }

    [Fact]
    public async Task UnknownIds_Are404()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = fixture.ApiServiceClient;

        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/test-scenarios/{Guid.NewGuid()}/runs", null, cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/test-runs/{Guid.NewGuid()}", cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/test-runs/{Guid.NewGuid()}/cancel", null, cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/test-runs?cursor=not-a-cursor", cancellationToken)).StatusCode);
    }

    private static Task<JsonNode> WaitForFinalStatusAsync(HttpClient client, Guid runId, CancellationToken cancellationToken)
        => WaitForStatusAsync(client, runId, null, cancellationToken);

    /// <summary>Polls the run until it reaches <paramref name="status"/> — or, when null, any final status.</summary>
    private static async Task<JsonNode> WaitForStatusAsync(HttpClient client, Guid runId, string? status, CancellationToken cancellationToken)
    {
        string[] finalStatuses = ["Passed", "Failed", "Cancelled", "Interrupted"];
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (true)
        {
            var run = (await client.GetFromJsonAsync<JsonNode>($"/api/test-runs/{runId}", cancellationToken))!;
            var current = run["status"]!.GetValue<string>();
            if (status is null ? finalStatuses.Contains(current) : current == status)
            {
                return run;
            }

            Assert.True(DateTime.UtcNow < deadline, $"Run {runId} is still {current}.");
            await Task.Delay(250, cancellationToken);
        }
    }

    private async Task<Guid> CreateHttpScenarioAsync(HttpClient client, Guid suffix, CancellationToken cancellationToken)
    {
        var (specificationId, endpointId) = await ImportAsync(client, "openapi", $"""
            openapi: 3.0.3
            info:
              title: "TestRuns Http Fixture {suffix}"
              version: "1.0.0"
            paths:
              /health:
                get:
                  responses:
                    "200":
                      description: OK
            """, cancellationToken);
        var connectionId = await CreateConnectionAsync(client, $"TestRuns Http {suffix}", "Http", fixture.ApiServiceHttpAddress.ToString(), cancellationToken);
        return await CreateScenarioAsync(client, new { Name = $"TestRuns Http {suffix}", SpecificationId = specificationId, MockEndpointId = endpointId, ConnectionId = connectionId }, cancellationToken);
    }

    private async Task<Guid> CreateListenScenarioAsync(HttpClient client, Guid suffix, int timeoutSeconds, CancellationToken cancellationToken)
    {
        var (specificationId, endpointId) = await ImportAsync(client, "asyncapi", $"""
            asyncapi: 3.0.0
            info:
              title: "TestRuns Listen Fixture {suffix}"
              version: "1.0.0"
            channels:
              silence:
                address: silence.{suffix:N}
                messages:
                  any:
                    payload:
                      type: object
            operations:
              publishSilence:
                action: send
                channel:
                  $ref: "#/channels/silence"
                messages:
                  - $ref: "#/channels/silence/messages/any"
            """, cancellationToken);
        var natsConnectionString = await fixture.App.GetConnectionStringAsync("nats", cancellationToken);
        var connectionId = await CreateConnectionAsync(client, $"TestRuns NATS {suffix}", "Nats", natsConnectionString!, cancellationToken);
        return await CreateScenarioAsync(client, new
        {
            Name = $"TestRuns Listen {suffix}",
            SpecificationId = specificationId,
            MockEndpointId = endpointId,
            ConnectionId = connectionId,
            Kind = "Listen",
            ListenTimeoutSeconds = timeoutSeconds
        }, cancellationToken);
    }

    private static async Task<(Guid SpecificationId, Guid EndpointId)> ImportAsync(HttpClient client, string kind, string yaml, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/specifications/{kind}")
        {
            Content = new StringContent(yaml, Encoding.UTF8, "text/plain"),
        };
        var response = await client.SendAsync(request, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var specificationId = (await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!["id"]!.GetValue<Guid>();
        var details = await client.GetFromJsonAsync<JsonNode>($"/api/specifications/{specificationId}", cancellationToken);
        return (specificationId, details!["endpoints"]!.AsArray().Single()!["id"]!.GetValue<Guid>());
    }

    private static async Task<Guid> CreateConnectionAsync(HttpClient client, string name, string serviceType, string value, CancellationToken cancellationToken)
    {
        var response = await client.PostAsJsonAsync("/api/connections", new { Name = name, ServiceType = serviceType, Value = value }, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!["id"]!.GetValue<Guid>();
    }

    private static async Task<Guid> CreateScenarioAsync(HttpClient client, object body, CancellationToken cancellationToken)
    {
        var response = await client.PostAsJsonAsync("/api/test-scenarios", body, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!["id"]!.GetValue<Guid>();
    }
}
