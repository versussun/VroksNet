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

    /// <summary>The next run times of a cron schedule in a time zone, or the reason it's invalid.</summary>
    public async Task<SchedulePreview> PreviewScheduleAsync(string schedule, string? timeZone, CancellationToken cancellationToken = default)
    {
        var query = $"schedule={Uri.EscapeDataString(schedule)}&timeZone={Uri.EscapeDataString(timeZone ?? string.Empty)}&count=3";
        var preview = await httpClient.GetFromJsonAsync<SchedulePreview>($"/api/test-scenarios/schedule-preview?{query}", JsonOptions, cancellationToken);
        return preview ?? new SchedulePreview("No answer from the server.", []);
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
        await ThrowIfRejectedAsync(response, cancellationToken);
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

        await ThrowIfRejectedAsync(response, cancellationToken);

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

    /// <summary>Queues a background run. Returns the run's id, or null if no scenario with that id exists.</summary>
    public async Task<Guid?> StartRunAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsync($"/api/test-scenarios/{id}/runs", null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<StartTestRunResult>(JsonOptions, cancellationToken))!.RunId;
    }

    /// <summary>Returns null if no run with that id exists.</summary>
    public async Task<TestRunSummary?> GetRunAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync($"/api/test-runs/{runId}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TestRunSummary>(JsonOptions, cancellationToken);
    }

    /// <summary>The newest runs of one scenario, newest first.</summary>
    public async Task<TestRunSummary[]> GetRunsAsync(Guid scenarioId, int limit, CancellationToken cancellationToken = default)
    {
        var page = await httpClient.GetFromJsonAsync<TestRunPage>($"/api/test-runs?testScenarioId={scenarioId}&limit={limit}", JsonOptions, cancellationToken);
        return page?.Items ?? [];
    }

    /// <summary>Asks a queued or running run to stop. False if it had already finished or doesn't exist.</summary>
    public async Task<bool> CancelRunAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsync($"/api/test-runs/{runId}/cancel", null, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    /// <summary>A 400 carries the reason (a blank or taken name, an incompatible operation/connection, ...) — surface it instead of a bare status code.</summary>
    private static async Task ThrowIfRejectedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemResponse>(JsonOptions, cancellationToken);
            throw new InvalidOperationException(problem?.Detail ?? "The server rejected the test scenario.");
        }
    }
}
