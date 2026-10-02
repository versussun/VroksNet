using System.Net.Http.Json;
using System.Text.Json.Nodes;
using VroksNet.IntegrationTests.Fixtures;

namespace VroksNet.IntegrationTests;

/// <summary>Turning one operation's mock off and on through PUT /api/mock-endpoints/{id}/enabled.</summary>
[Collection("AppHost")]
public sealed class EndpointEnabledApiTests(AppHostFixture fixture)
{
    [Fact]
    public async Task DisabledOperation_Answers404_UntilEnabledAgain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = fixture.ApiServiceClient;
        var suffix = Guid.NewGuid().ToString("N");
        var yaml = $$"""
            openapi: 3.0.3
            info:
              title: "Enabled Fixture {{suffix}}"
              version: "1.0.0"
            paths:
              /enabled-{{suffix}}/pets:
                get:
                  responses:
                    "200":
                      description: OK
                      content:
                        application/json:
                          example: [{ name: Fido }]
            """;
        using var import = new HttpRequestMessage(HttpMethod.Post, "/api/specifications/openapi")
        {
            Content = new StringContent(yaml, System.Text.Encoding.UTF8, "text/plain"),
        };
        import.Headers.Add("X-File-Name", "enabled.yaml");
        var imported = await client.SendAsync(import, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, imported.StatusCode);
        var specificationId = (await imported.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!["id"]!.GetValue<Guid>();
        var details = await client.GetFromJsonAsync<JsonNode>($"/api/specifications/{specificationId}", cancellationToken);
        var endpointId = details!["endpoints"]![0]!["id"]!.GetValue<Guid>();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/mock/enabled-{suffix}/pets", cancellationToken)).StatusCode);

        var disable = await client.PutAsJsonAsync($"/api/mock-endpoints/{endpointId}/enabled", new { enabled = false }, cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, disable.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/mock/enabled-{suffix}/pets", cancellationToken)).StatusCode);
        var afterDisable = await client.GetFromJsonAsync<JsonNode>($"/api/specifications/{specificationId}", cancellationToken);
        Assert.False(afterDisable!["endpoints"]![0]!["isEnabled"]!.GetValue<bool>());

        var enable = await client.PutAsJsonAsync($"/api/mock-endpoints/{endpointId}/enabled", new { enabled = true }, cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, enable.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/mock/enabled-{suffix}/pets", cancellationToken)).StatusCode);
    }

    [Fact]
    public async Task MissingBody_Is400_AndUnknownEndpoint_Is404()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = fixture.ApiServiceClient;

        var noField = await client.PutAsJsonAsync($"/api/mock-endpoints/{Guid.NewGuid()}/enabled", new { enable = false }, cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, noField.StatusCode);

        var unknown = await client.PutAsJsonAsync($"/api/mock-endpoints/{Guid.NewGuid()}/enabled", new { enabled = false }, cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }
}
