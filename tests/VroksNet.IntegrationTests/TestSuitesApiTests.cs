using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using VroksNet.IntegrationTests.Fixtures;

namespace VroksNet.IntegrationTests;

/// <summary>
/// Test suites (ADR 0002, step B4) against the AppHost's real NATS: a suite whose Send is listed
/// before its Listen still passes — the Listen subscribes first and catches the Send's message —
/// and a failing suite names what failed, the way a CI pipeline reads it.
/// </summary>
[Collection("AppHost")]
public sealed class TestSuitesApiTests(AppHostFixture fixture)
{
    [Fact]
    public async Task ASuite_ListensBeforeItSends_AndCiReadsTheLatestRunByName()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = fixture.ApiServiceClient;
        var suffix = Guid.NewGuid().ToString("N");
        var (send, listen) = await CreateSendAndListenAsync(client, suffix, cancellationToken);
        var suiteName = $"chain-{suffix}";

        var create = await client.PostAsJsonAsync("/api/test-suites", new { Name = suiteName, TestScenarioIds = new[] { send, listen } }, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/test-suites/{suiteName}/runs/latest", cancellationToken)).StatusCode);

        // Started by name, as a pipeline would.
        var start = await client.PostAsync($"/api/test-suites/{suiteName}/runs", null, cancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        var suiteRunId = (await start.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!["suiteRunId"]!.GetValue<Guid>();
        Assert.Equal($"/api/suite-runs/{suiteRunId}", start.Headers.Location!.OriginalString);

        var run = await WaitForFinalStatusAsync(client, $"/api/suite-runs/{suiteRunId}", cancellationToken);
        Assert.True(run["status"]!.GetValue<string>() == "Passed", run.ToJsonString());
        Assert.Equal("All 2 passed.", run["message"]!.GetValue<string>());
        Assert.Empty(run["failed"]!.AsArray());
        var runs = run["runs"]!.AsArray();
        Assert.Equal([$"send-{suffix}", $"listen-{suffix}"], runs.Select(entry => entry!["scenarioName"]!.GetValue<string>()));

        var latest = (await client.GetFromJsonAsync<JsonNode>($"/api/test-suites/{suiteName}/runs/latest", cancellationToken))!;
        Assert.Equal(suiteRunId, latest["id"]!.GetValue<Guid>());

        // Each scenario's run is in its own history too, marked as part of the suite.
        var listenRunId = runs[1]!["testRunId"]!.GetValue<Guid>();
        var listenRun = (await client.GetFromJsonAsync<JsonNode>($"/api/test-runs/{listenRunId}", cancellationToken))!;
        Assert.Equal("Suite", listenRun["trigger"]!.GetValue<string>());
    }

    [Fact]
    public async Task AFailingSuite_NamesWhatFailed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = fixture.ApiServiceClient;
        var suffix = Guid.NewGuid().ToString("N");
        // A Listen nothing ever publishes to, with a short wait.
        var (_, listen) = await CreateSendAndListenAsync(client, suffix, cancellationToken, listenTimeoutSeconds: 2);
        var suiteName = $"silence-{suffix}";
        var suiteId = (await (await client.PostAsJsonAsync("/api/test-suites", new { Name = suiteName, TestScenarioIds = new[] { listen } }, cancellationToken))
            .Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!["id"]!.GetValue<Guid>();

        var start = await client.PostAsync($"/api/test-suites/{suiteId}/runs", null, cancellationToken);
        var suiteRunId = (await start.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!["suiteRunId"]!.GetValue<Guid>();
        var run = await WaitForFinalStatusAsync(client, $"/api/suite-runs/{suiteRunId}", cancellationToken);

        Assert.Equal("Failed", run["status"]!.GetValue<string>());
        Assert.Equal([$"listen-{suffix}"], run["failed"]!.AsArray().Select(name => name!.GetValue<string>()));
        Assert.Contains("No message on", run["runs"]![0]!["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task BadSuites_AreA400WithTheReason_AndUnknownSuitesA404()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = fixture.ApiServiceClient;

        var empty = await client.PostAsJsonAsync("/api/test-suites", new { Name = $"empty-{Guid.NewGuid():N}", TestScenarioIds = Array.Empty<Guid>() }, cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Contains("at least one test scenario", (await empty.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!["detail"]!.GetValue<string>());

        var unknown = $"no-such-suite-{Guid.NewGuid():N}";
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/test-suites/{unknown}/runs", null, cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/test-suites/{unknown}/runs/latest", cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/suite-runs/{Guid.NewGuid()}", cancellationToken)).StatusCode);
    }

    /// <summary>A NATS channel with a Send scenario publishing its example and a Listen scenario waiting for it.</summary>
    private async Task<(Guid Send, Guid Listen)> CreateSendAndListenAsync(HttpClient client, string suffix, CancellationToken cancellationToken, int listenTimeoutSeconds = 20)
    {
        using var import = new HttpRequestMessage(HttpMethod.Post, "/api/specifications/asyncapi")
        {
            Content = new StringContent($$"""
                asyncapi: 3.0.0
                info: { title: "Suite events {{suffix}}", version: "1" }
                channels:
                  created:
                    address: suite.{{suffix}}.created
                    messages:
                      created:
                        payload:
                          type: object
                          required: [id]
                          properties: { id: { type: string } }
                        examples:
                          - payload: { id: "order-1" }
                operations:
                  publishCreated:
                    action: send
                    channel: { $ref: "#/channels/created" }
                    messages:
                      - $ref: "#/channels/created/messages/created"
                """, Encoding.UTF8, "text/plain")
        };
        var imported = await client.SendAsync(import, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, imported.StatusCode);
        var specificationId = (await imported.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!["id"]!.GetValue<Guid>();
        var endpointId = (await client.GetFromJsonAsync<JsonNode>($"/api/specifications/{specificationId}", cancellationToken))!["endpoints"]!.AsArray().Single()!["id"]!.GetValue<Guid>();

        var nats = await fixture.App.GetConnectionStringAsync("nats", cancellationToken);
        var connectionId = await PostIdAsync(client, "/api/connections", new { Name = $"Suite NATS {suffix}", ServiceType = "Nats", Value = nats }, cancellationToken);

        var send = await PostIdAsync(client, "/api/test-scenarios", new { Name = $"send-{suffix}", SpecificationId = specificationId, MockEndpointId = endpointId, ConnectionId = connectionId, Kind = "Send" }, cancellationToken);
        var listen = await PostIdAsync(client, "/api/test-scenarios", new { Name = $"listen-{suffix}", SpecificationId = specificationId, MockEndpointId = endpointId, ConnectionId = connectionId, Kind = "Listen", ListenTimeoutSeconds = listenTimeoutSeconds }, cancellationToken);
        return (send, listen);
    }

    private static async Task<Guid> PostIdAsync(HttpClient client, string path, object body, CancellationToken cancellationToken)
    {
        var response = await client.PostAsJsonAsync(path, body, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!["id"]!.GetValue<Guid>();
    }

    private static async Task<JsonNode> WaitForFinalStatusAsync(HttpClient client, string path, CancellationToken cancellationToken)
    {
        string[] finalStatuses = ["Passed", "Failed", "Cancelled", "Interrupted"];
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (true)
        {
            var run = (await client.GetFromJsonAsync<JsonNode>(path, cancellationToken))!;
            if (finalStatuses.Contains(run["status"]!.GetValue<string>()))
            {
                return run;
            }

            Assert.True(DateTime.UtcNow < deadline, $"Still {run["status"]}: {run.ToJsonString()}");
            await Task.Delay(250, cancellationToken);
        }
    }
}
