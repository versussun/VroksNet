using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VroksNet.Web;

public sealed class SpecificationApiClient(HttpClient httpClient)
{
    // ApiService serializes enums as strings (see Program.cs ConfigureHttpJsonOptions) —
    // this client needs the matching converter or SpecificationKind fails to deserialize.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<SpecificationSummary[]> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var specifications = await httpClient.GetFromJsonAsync<SpecificationSummary[]>("/api/specifications", JsonOptions, cancellationToken);
        return specifications ?? [];
    }

    /// <summary>Returns null if no specification with that id exists.</summary>
    public async Task<SpecificationDetails?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync($"/api/specifications/{id}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SpecificationDetails>(JsonOptions, cancellationToken);
    }

    /// <summary>Sends a real HTTP request to the mock-serving route ("/mock/...") and returns the raw status + body — never throws on a non-success status.</summary>
    public async Task<MockInvocationResponse> InvokeMockAsync(string method, string path, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), $"/mock/{path.TrimStart('/')}");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return new MockInvocationResponse((int)response.StatusCode, body);
    }

    /// <summary>Imports an OpenAPI YAML file. Throws if the server rejects it (e.g. it doesn't parse).</summary>
    public async Task<Guid> ImportOpenApiAsync(Stream yamlContent, string fileName, CancellationToken cancellationToken = default)
    {
        using var content = new StreamContent(yamlContent);
        content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/specifications/openapi")
        {
            Content = content
        };
        request.Headers.Add("X-File-Name", fileName);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ImportSpecificationResult>(cancellationToken);
        return result!.Id;
    }
}
