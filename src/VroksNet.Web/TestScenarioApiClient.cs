using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VroksNet.Web;

public sealed class TestScenarioApiClient(HttpClient httpClient)
{
    // Matches ApiService's ConfigureHttpJsonOptions — see SpecificationApiClient for why this is needed.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<TestScenarioSummary[]> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var scenarios = await httpClient.GetFromJsonAsync<TestScenarioSummary[]>("/api/test-scenarios", JsonOptions, cancellationToken);
        return scenarios ?? [];
    }

    public async Task<Guid> CreateAsync(string name, Guid specificationId, Guid mockEndpointId, Guid connectionId, string? payloadOverride, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "/api/test-scenarios",
            new { name, specificationId, mockEndpointId, connectionId, payloadOverride },
            JsonOptions,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CreateTestScenarioResult>(JsonOptions, cancellationToken);
        return result!.Id;
    }

    /// <summary>Returns false if no scenario with that id exists.</summary>
    public async Task<bool> UpdateAsync(Guid id, string name, Guid specificationId, Guid mockEndpointId, Guid connectionId, string? payloadOverride, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PutAsJsonAsync(
            $"/api/test-scenarios/{id}",
            new { id, name, specificationId, mockEndpointId, connectionId, payloadOverride },
            JsonOptions,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        return true;
    }

    /// <summary>Returns false if no scenario with that id exists.</summary>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.DeleteAsync($"/api/test-scenarios/{id}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        return true;
    }

    /// <summary>Returns null if no scenario with that id exists.</summary>
    public async Task<RunTestScenarioResult?> RunAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsync($"/api/test-scenarios/{id}/run", null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RunTestScenarioResult>(JsonOptions, cancellationToken);
    }
}
