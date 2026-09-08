using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace VroksNet.Web;

public sealed class SpecificationApiClient(HttpClient httpClient)
{
    public async Task<SpecificationSummary[]> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var specifications = await httpClient.GetFromJsonAsync<SpecificationSummary[]>("/api/specifications", cancellationToken);
        return specifications ?? [];
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
