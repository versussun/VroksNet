using System.Net.Http.Json;
using System.Text.Json.Nodes;
using VroksNet.IntegrationTests.Fixtures;

namespace VroksNet.IntegrationTests;

/// <summary>Provider mode ("Тип 3"): the separate provider port answers at real spec paths, and nothing else is reachable on it.</summary>
[Collection("AppHost")]
public sealed class ProviderModeApiTests(AppHostFixture fixture)
{
    [Fact]
    public async Task ProviderPort_ServesOnlyTheMock()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var provider = new HttpClient { BaseAddress = fixture.ProviderAddress };

        // ApiService's own routes aren't reachable on the provider port — the mock answers instead.
        var health = await provider.GetAsync("/health", cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, health.StatusCode);
        var problem = await health.Content.ReadFromJsonAsync<JsonNode>(cancellationToken);
        Assert.Contains("Serve at real path", problem!["detail"]!.GetValue<string>());

        // …while the main port still serves them.
        Assert.Equal(HttpStatusCode.OK, (await fixture.ApiServiceClient.GetAsync("/health", cancellationToken)).StatusCode);

        // The Admin UI learns where to point services from here (AppHost sets the public URL).
        var info = await fixture.ApiServiceClient.GetFromJsonAsync<JsonNode>("/api/system/provider", cancellationToken);
        Assert.True(info!["enabled"]!.GetValue<bool>());
        Assert.Equal("http://localhost:7353", info["publicUrl"]!.GetValue<string>());
    }

    [Fact]
    public async Task ProviderModeBody_WithoutEnabled_Is400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = fixture.ApiServiceClient;
        var (_, endpoints) = await ImportAsync(client, Guid.NewGuid().ToString("N"), cancellationToken);

        var response = await client.PutAsJsonAsync($"/api/mock-endpoints/{endpoints.Values.First()}/provider-mode", new { enable = true }, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ProviderPort_FormBodyIsNotAContractViolation_AndHeadAnswersLikeGet()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = fixture.ApiServiceClient;
        var suffix = Guid.NewGuid().ToString("N");
        var (specificationId, endpoints) = await ImportAsync(client, suffix, cancellationToken);
        await client.PutAsJsonAsync($"/api/specifications/{specificationId}/provider-mode", new { enabled = true }, cancellationToken);
        using var provider = new HttpClient { BaseAddress = fixture.ProviderAddress };

        try
        {
            var form = await provider.PostAsync($"/provider-{suffix}/pets", new FormUrlEncodedContent([new("name", "Fido")]), cancellationToken);
            Assert.Equal(HttpStatusCode.OK, form.StatusCode);
            var history = await client.GetFromJsonAsync<JsonNode>($"/api/call-records?specificationId={specificationId}", cancellationToken);
            Assert.Null(history!["items"]![0]!["contractValid"]); // not JSON → not checked

            using var head = new HttpRequestMessage(HttpMethod.Head, $"/provider-{suffix}/pets/7");
            Assert.Equal(HttpStatusCode.OK, (await provider.SendAsync(head, cancellationToken)).StatusCode);
        }
        finally
        {
            await client.PutAsJsonAsync($"/api/specifications/{specificationId}/provider-mode", new { enabled = false }, cancellationToken);
        }
    }

    [Fact]
    public async Task ServedOperation_AnswersAtItsRealPath_AndLogsRequestValidation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = fixture.ApiServiceClient;
        var suffix = Guid.NewGuid().ToString("N");
        var (specificationId, endpoints) = await ImportAsync(client, suffix, cancellationToken);
        var postPets = endpoints[$"POST /provider-{suffix}/pets"];
        using var provider = new HttpClient { BaseAddress = fixture.ProviderAddress };

        // Not served yet: the provider port doesn't answer it.
        Assert.Equal(HttpStatusCode.NotFound, (await provider.PostAsJsonAsync($"/provider-{suffix}/pets", new { name = "Fido" }, cancellationToken)).StatusCode);

        var enable = await client.PutAsJsonAsync($"/api/mock-endpoints/{postPets}/provider-mode", new { enabled = true }, cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, enable.StatusCode);

        var valid = await provider.PostAsJsonAsync($"/provider-{suffix}/pets", new { name = "Fido" }, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        Assert.Contains("Fido", await valid.Content.ReadAsStringAsync(cancellationToken));

        // An invalid body is still answered — the violation goes to the history.
        var invalid = await provider.PostAsJsonAsync($"/provider-{suffix}/pets", new { tag = "dog" }, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);

        var history = await client.GetFromJsonAsync<JsonNode>($"/api/call-records?specificationId={specificationId}", cancellationToken);
        var items = history!["items"]!.AsArray();
        Assert.Equal($"POST /provider-{suffix}/pets", items[0]!["requestLine"]!.GetValue<string>()); // real path, no "/mock"
        Assert.False(items[0]!["contractValid"]!.GetValue<bool>());
        Assert.True(items[1]!["contractValid"]!.GetValue<bool>());

        // The details endpoint marks it as served; turning it off stops the provider port answering.
        var details = await client.GetFromJsonAsync<JsonNode>($"/api/specifications/{specificationId}", cancellationToken);
        Assert.True(details!["endpoints"]!.AsArray().Single(e => e!["id"]!.GetValue<Guid>() == postPets)!["serveAtRealPath"]!.GetValue<bool>());
        await client.PutAsJsonAsync($"/api/mock-endpoints/{postPets}/provider-mode", new { enabled = false }, cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, (await provider.PostAsJsonAsync($"/provider-{suffix}/pets", new { name = "Fido" }, cancellationToken)).StatusCode);
    }

    [Fact]
    public async Task ServedOperation_RendersItsTemplate_AtTheSpecStatus()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = fixture.ApiServiceClient;
        var suffix = Guid.NewGuid().ToString("N");
        var yaml = $$$"""
            openapi: 3.0.3
            info:
              title: "Templating Fixture {{{suffix}}}"
              version: "1.0.0"
            paths:
              /templating-{{{suffix}}}/owners/{ownerId}/pets:
                post:
                  responses:
                    "201":
                      description: Created
                      content:
                        application/json:
                          example:
                            id: "{{uuid}}"
                            owner: "{{request.path.ownerId}}"
                            name: "{{request.body.$.name}}"
                            lang: "{{request.query.lang}}"
                            missing: "{{request.header.X-Absent}}"
            """;
        using var import = new HttpRequestMessage(HttpMethod.Post, "/api/specifications/openapi")
        {
            Content = new StringContent(yaml, System.Text.Encoding.UTF8, "text/plain"),
        };
        import.Headers.Add("X-File-Name", "templating.yaml");
        var imported = await client.SendAsync(import, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, imported.StatusCode);
        var specificationId = (await imported.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!["id"]!.GetValue<Guid>();
        await client.PutAsJsonAsync($"/api/specifications/{specificationId}/provider-mode", new { enabled = true }, cancellationToken);
        using var provider = new HttpClient { BaseAddress = fixture.ProviderAddress };

        try
        {
            var response = await provider.PostAsJsonAsync($"/templating-{suffix}/owners/7/pets?lang=uk", new { name = "Fido" }, cancellationToken);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken);
            Assert.True(Guid.TryParse(body!["id"]!.GetValue<string>(), out _));
            Assert.Equal("7", body["owner"]!.GetValue<string>());
            Assert.Equal("Fido", body["name"]!.GetValue<string>());
            Assert.Equal("uk", body["lang"]!.GetValue<string>());
            Assert.Equal(string.Empty, body["missing"]!.GetValue<string>());

            // The history keeps the rendered response, its status, and the unfilled placeholder as a warning.
            var history = await client.GetFromJsonAsync<JsonNode>($"/api/call-records?specificationId={specificationId}", cancellationToken);
            var record = history!["items"]![0]!;
            Assert.Equal(201, record["statusCode"]!.GetValue<int>());
            Assert.Contains("X-Absent", Assert.Single(record["warnings"]!.AsArray())!.GetValue<string>());
            var details = await client.GetFromJsonAsync<JsonNode>($"/api/call-records/{record["id"]!.GetValue<Guid>()}", cancellationToken);
            Assert.Contains("Fido", details!["responseSnapshot"]!.GetValue<string>());
        }
        finally
        {
            await client.PutAsJsonAsync($"/api/specifications/{specificationId}/provider-mode", new { enabled = false }, cancellationToken);
        }
    }

    [Fact]
    public async Task EnablingAnOverlappingOperation_Is409_AndServeAllReportsItAsSkipped()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = fixture.ApiServiceClient;
        var suffix = Guid.NewGuid().ToString("N");
        var (firstSpecId, firstEndpoints) = await ImportAsync(client, suffix, cancellationToken, titlePrefix: "Provider A");
        var (secondSpecId, secondEndpoints) = await ImportAsync(client, suffix, cancellationToken, titlePrefix: "Provider B");

        var serveAll = await client.PutAsJsonAsync($"/api/specifications/{firstSpecId}/provider-mode", new { enabled = true }, cancellationToken);
        var served = await serveAll.Content.ReadFromJsonAsync<JsonNode>(cancellationToken);
        Assert.Equal(2, served!["served"]!.AsArray().Count);
        Assert.Empty(served["skipped"]!.AsArray());

        // Spec B declares the same operations — enabling one is refused, naming spec A's.
        var conflict = await client.PutAsJsonAsync($"/api/mock-endpoints/{secondEndpoints[$"GET /provider-{suffix}/pets/{{id}}"]}/provider-mode", new { enabled = true }, cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var problem = await conflict.Content.ReadFromJsonAsync<JsonNode>(cancellationToken);
        Assert.Contains("Provider A", problem!["detail"]!.GetValue<string>());

        var serveAllB = await client.PutAsJsonAsync($"/api/specifications/{secondSpecId}/provider-mode", new { enabled = true }, cancellationToken);
        var skipped = (await serveAllB.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!["skipped"]!.AsArray();
        Assert.Equal(2, skipped.Count);

        // Clean up so later tests' provider-port calls can't hit these operations.
        await client.PutAsJsonAsync($"/api/specifications/{firstSpecId}/provider-mode", new { enabled = false }, cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/api/specifications/{Guid.NewGuid()}/provider-mode", new { enabled = true }, cancellationToken)).StatusCode);
    }

    /// <summary>Two operations under a GUID-suffixed path; returns the spec id and operation key → endpoint id.</summary>
    private static async Task<(Guid SpecificationId, Dictionary<string, Guid> Endpoints)> ImportAsync(HttpClient client, string suffix, CancellationToken cancellationToken, string titlePrefix = "Provider")
    {
        var yaml = $$"""
            openapi: 3.0.3
            info:
              title: "{{titlePrefix}} Fixture {{suffix}}"
              version: "1.0.0"
            paths:
              /provider-{{suffix}}/pets:
                post:
                  requestBody:
                    content:
                      application/json:
                        schema:
                          type: object
                          required: [name]
                          properties:
                            name:
                              type: string
                  responses:
                    "200":
                      description: OK
                      content:
                        application/json:
                          example:
                            name: Fido
              /provider-{{suffix}}/pets/{id}:
                get:
                  responses:
                    "200":
                      description: OK
            """;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/specifications/openapi")
        {
            Content = new StringContent(yaml, System.Text.Encoding.UTF8, "text/plain"),
        };
        request.Headers.Add("X-File-Name", "provider.yaml");
        var response = await client.SendAsync(request, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var specificationId = (await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!["id"]!.GetValue<Guid>();

        var details = await client.GetFromJsonAsync<JsonNode>($"/api/specifications/{specificationId}", cancellationToken);
        var endpoints = details!["endpoints"]!.AsArray().ToDictionary(e => e!["operationKey"]!.GetValue<string>(), e => e!["id"]!.GetValue<Guid>());
        return (specificationId, endpoints);
    }
}
