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

    /// <summary>Includes the scenario's last-run status. Returns null if no scenario with that id exists.</summary>
    public async Task<TestScenarioSummary?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync($"/api/test-scenarios/{id}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TestScenarioSummary>(JsonOptions, cancellationToken);
    }

    public async Task<Guid> CreateAsync(TestScenarioForm form, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "/api/test-scenarios",
            form,
            JsonOptions,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CreateTestScenarioResult>(JsonOptions, cancellationToken);
        return result!.Id;
    }

    /// <summary>Returns false if no scenario with that id exists.</summary>
    public async Task<bool> UpdateAsync(Guid id, TestScenarioForm form, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PutAsJsonAsync(
            $"/api/test-scenarios/{id}",
            form,
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
