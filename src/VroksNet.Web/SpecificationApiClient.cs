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
    public Task<Guid> ImportOpenApiAsync(Stream yamlContent, string fileName, CancellationToken cancellationToken = default)
        => ImportAsync("/api/specifications/openapi", yamlContent, fileName, cancellationToken);

    /// <summary>Imports an AsyncAPI YAML file. Throws if the server rejects it (e.g. it doesn't parse).</summary>
    public Task<Guid> ImportAsyncApiAsync(Stream yamlContent, string fileName, CancellationToken cancellationToken = default)
        => ImportAsync("/api/specifications/asyncapi", yamlContent, fileName, cancellationToken);

    private async Task<Guid> ImportAsync(string requestUri, Stream yamlContent, string fileName, CancellationToken cancellationToken)
    {
        using var content = new StreamContent(yamlContent);
        content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = content
        };
        request.Headers.Add("X-File-Name", fileName);

        using var response = await httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            // A file that failed to parse (see SpecificationEndpoints) — surface the parser's own
            // message rather than EnsureSuccessStatusCode()'s generic "500/400 (...)" text.
            var error = await response.Content.ReadFromJsonAsync<ImportSpecificationError>(JsonOptions, cancellationToken);
            throw new InvalidOperationException(error?.Message ?? "The server rejected this file.");
        }

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ImportSpecificationResult>(cancellationToken);
        return result!.Id;
    }

    /// <summary>Turns provider mode on/off for one operation. Returns null on success, or the server's reason when it refused (409) — e.g. an overlap with an operation already served.</summary>
    public async Task<string?> SetEndpointProviderModeAsync(Guid mockEndpointId, bool enabled, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PutAsJsonAsync($"/api/mock-endpoints/{mockEndpointId}/provider-mode", new { enabled }, JsonOptions, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return "This operation no longer exists — the specification may have been re-imported or deleted. Reload the page.";
        }

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemResponse>(JsonOptions, cancellationToken);
            return problem?.Detail ?? "The server refused the change.";
        }

        response.EnsureSuccessStatusCode();
        return null;
    }

    /// <summary>Turns provider mode on/off for every HTTP operation of a specification. Returns null if it no longer exists.</summary>
    /// <exception cref="InvalidOperationException">The server refused (409) — its reason is the message.</exception>
    public async Task<SpecificationProviderModeResult?> SetSpecificationProviderModeAsync(Guid specificationId, bool enabled, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PutAsJsonAsync($"/api/specifications/{specificationId}/provider-mode", new { enabled }, JsonOptions, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemResponse>(JsonOptions, cancellationToken);
            throw new InvalidOperationException(problem?.Detail ?? "The server refused the change.");
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SpecificationProviderModeResult>(JsonOptions, cancellationToken);
    }
}
