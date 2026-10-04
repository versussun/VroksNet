using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using VroksNet.IntegrationTests.Fixtures;

namespace VroksNet.IntegrationTests;

/// <summary>
/// Drives the real specification-import → list → detail → mock-invocation flow over HTTP against
/// the booted ApiService — a black-box test of the wire contract, so it talks JSON/YAML rather
/// than referencing Application/Domain types directly (see .claude/CLAUDE.md "Testing"). Re-import
/// of the same title is idempotent (replace-by-title semantics — see .claude/CLAUDE.md
/// "Infrastructure notes"), so a fixed title is safe to reuse across test runs.
/// </summary>
[Collection("AppHost")]
public sealed class SpecificationsApiTests(AppHostFixture fixture)
{
    private const string PetstoreYaml = """
        openapi: 3.0.3
        info:
          title: VroksNet.IntegrationTests Petstore Fixture
          version: "1.0.0"
        paths:
          /pets:
            get:
              operationId: listPets
              summary: List all pets
              responses:
                "200":
                  description: A page of pets
                  content:
                    application/json:
                      schema:
                        type: array
                        items:
                          type: object
                      example:
                        - id: 1
                          name: Fido
        """;

    [Fact]
    public async Task ImportOpenApiSpec_Then_ListDetailAndInvokeMock_Succeed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = fixture.ApiServiceClient;

        // Import
        using var importRequest = new HttpRequestMessage(HttpMethod.Post, "/api/specifications/openapi")
        {
            Content = new StringContent(PetstoreYaml, Encoding.UTF8, "text/plain"),
        };
        importRequest.Headers.Add("X-File-Name", "petstore.yaml");
        var importResponse = await client.SendAsync(importRequest, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, importResponse.StatusCode);
        var imported = await importResponse.Content.ReadFromJsonAsync<JsonNode>(cancellationToken);
        var id = imported!["id"]!.GetValue<Guid>();

        // List includes it
        var listResponse = await client.GetAsync("/api/specifications", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var specifications = await listResponse.Content.ReadFromJsonAsync<JsonArray>(cancellationToken);
        var summary = specifications!.Single(specification => specification!["id"]!.GetValue<Guid>() == id);
        Assert.Equal("VroksNet.IntegrationTests Petstore Fixture", summary!["title"]!.GetValue<string>());
        Assert.Equal("OpenApi", summary["kind"]!.GetValue<string>());

        // Detail includes the parsed endpoint, enabled by default
        var detailResponse = await client.GetAsync($"/api/specifications/{id}", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var details = await detailResponse.Content.ReadFromJsonAsync<JsonNode>(cancellationToken);
        var endpoint = details!["endpoints"]!.AsArray().Single();
        Assert.Equal("GET /pets", endpoint!["operationKey"]!.GetValue<string>());
        Assert.True(endpoint["isEnabled"]!.GetValue<bool>());

        // Unknown id 404s
        var missingDetailResponse = await client.GetAsync($"/api/specifications/{Guid.NewGuid()}", cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, missingDetailResponse.StatusCode);

        // The mock endpoint answers with the spec's example
        var mockResponse = await client.GetAsync("/mock/pets", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, mockResponse.StatusCode);
        var mockBody = await mockResponse.Content.ReadFromJsonAsync<JsonArray>(cancellationToken);
        Assert.Equal("Fido", mockBody!.Single()!["name"]!.GetValue<string>());

        // An unmatched mock route 404s
        var unmatchedMockResponse = await client.GetAsync("/mock/does-not-exist", cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, unmatchedMockResponse.StatusCode);
    }

    [Fact]
    public async Task ImportedOperationWithoutAnExample_TheMockAnswersWithOneBuiltFromItsSchema()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = fixture.ApiServiceClient;
        var suffix = Guid.NewGuid().ToString("N");
        using var importRequest = new HttpRequestMessage(HttpMethod.Post, "/api/specifications/openapi")
        {
            Content = new StringContent($$"""
                openapi: 3.0.3
                info: { title: "Generated example {{suffix}}", version: "1" }
                paths:
                  /generated-{{suffix}}/orders/{id}:
                    get:
                      responses:
                        "200":
                          description: No example — only a schema.
                          content:
                            application/json:
                              schema:
                                type: object
                                required: [id, status, placedAt]
                                properties:
                                  id: { type: string, format: uuid }
                                  status: { type: string, enum: [placed, paid] }
                                  placedAt: { type: string, format: date-time }
                """, Encoding.UTF8, "text/plain"),
        };
        var importResponse = await client.SendAsync(importRequest, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, importResponse.StatusCode);
        var id = (await importResponse.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!["id"]!.GetValue<Guid>();

        var first = (await client.GetFromJsonAsync<JsonNode>($"/mock/generated-{suffix}/orders/1", cancellationToken))!;
        var second = (await client.GetFromJsonAsync<JsonNode>($"/mock/generated-{suffix}/orders/1", cancellationToken))!;

        // {{uuid}}/{{now}} in the built example are filled in per call.
        Assert.True(Guid.TryParse(first["id"]!.GetValue<string>(), out _), first.ToJsonString());
        Assert.NotEqual(first["id"]!.GetValue<string>(), second["id"]!.GetValue<string>());
        Assert.True(DateTimeOffset.TryParse(first["placedAt"]!.GetValue<string>(), out _));
        Assert.Equal("placed", first["status"]!.GetValue<string>());
        var endpoint = (await client.GetFromJsonAsync<JsonNode>($"/api/specifications/{id}", cancellationToken))!["endpoints"]!.AsArray().Single()!;
        Assert.True(endpoint["exampleIsGenerated"]!.GetValue<bool>());
    }
}
