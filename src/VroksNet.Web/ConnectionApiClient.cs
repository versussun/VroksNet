using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VroksNet.Web;

public sealed class ConnectionApiClient(HttpClient httpClient)
{
    // Matches ApiService's ConfigureHttpJsonOptions — see SpecificationApiClient for why this is needed.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<ConnectionSummary[]> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var connections = await httpClient.GetFromJsonAsync<ConnectionSummary[]>("/api/connections", JsonOptions, cancellationToken);
        return connections ?? [];
    }

    public async Task<Guid> CreateAsync(string name, ConnectionServiceType serviceType, string value, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("/api/connections", new { name, serviceType, value }, JsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CreateConnectionResult>(JsonOptions, cancellationToken);
        return result!.Id;
    }

    /// <summary>Returns false if no connection with that id exists.</summary>
    public async Task<bool> UpdateAsync(Guid id, string name, ConnectionServiceType serviceType, string value, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PutAsJsonAsync($"/api/connections/{id}", new { name, serviceType, value }, JsonOptions, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        return true;
    }

    /// <summary>Returns false if no connection with that id exists.</summary>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.DeleteAsync($"/api/connections/{id}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        return true;
    }
}
