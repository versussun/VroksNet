using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VroksNet.Web;

/// <summary>/api/test-suites and /api/suite-runs (ADR 0002, "Suites").</summary>
public sealed class TestSuiteApiClient(HttpClient httpClient)
{
    // Matches ApiService's ConfigureHttpJsonOptions — see SpecificationApiClient for why this is needed.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<TestSuiteSummary[]> GetAllAsync(CancellationToken cancellationToken = default)
        => await httpClient.GetFromJsonAsync<TestSuiteSummary[]>("/api/test-suites", JsonOptions, cancellationToken) ?? [];

    /// <summary>A blank or taken name, no scenarios… throws with the server's reason.</summary>
    public async Task CreateAsync(TestSuiteForm form, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("/api/test-suites", form, JsonOptions, cancellationToken);
        await ThrowIfRejectedAsync(response, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>False if the suite no longer exists.</summary>
    public async Task<bool> UpdateAsync(Guid id, TestSuiteForm form, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PutAsJsonAsync($"/api/test-suites/{id}", form, JsonOptions, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        await ThrowIfRejectedAsync(response, cancellationToken);
        response.EnsureSuccessStatusCode();
        return true;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.DeleteAsync($"/api/test-suites/{id}", cancellationToken);
        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            response.EnsureSuccessStatusCode();
        }
    }

    /// <summary>Queues a run; null if the suite no longer exists.</summary>
    public async Task<Guid?> StartRunAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsync($"/api/test-suites/{id}/runs", null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<StartSuiteRunResult>(JsonOptions, cancellationToken))!.SuiteRunId;
    }

    public async Task<SuiteRunDetails?> GetRunAsync(Guid suiteRunId, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync($"/api/suite-runs/{suiteRunId}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SuiteRunDetails>(JsonOptions, cancellationToken);
    }

    public async Task<SuiteRunDetails[]> GetRunsAsync(Guid suiteId, int limit, CancellationToken cancellationToken = default)
        => await httpClient.GetFromJsonAsync<SuiteRunDetails[]>($"/api/test-suites/{suiteId}/runs?limit={limit}", JsonOptions, cancellationToken) ?? [];

    public async Task CancelRunAsync(Guid suiteRunId, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsync($"/api/suite-runs/{suiteRunId}/cancel", null, cancellationToken);
    }

    private static async Task ThrowIfRejectedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemResponse>(JsonOptions, cancellationToken);
            throw new InvalidOperationException(problem?.Detail ?? "The server rejected the test suite.");
        }
    }
}
