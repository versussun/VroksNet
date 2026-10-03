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
        await ThrowIfRejectedAsync(response, cancellationToken);
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

        await ThrowIfRejectedAsync(response, cancellationToken);

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

    /// <summary>Returns null if no connection with that id exists.</summary>
    public async Task<ConnectionTestResult?> TestAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsync($"/api/connections/{id}/test", null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ConnectionTestResult>(JsonOptions, cancellationToken);
    }

    /// <summary>Tests a URL/connection string directly — for the Add/Edit panel's "Test" button, before the connection has been saved.</summary>
    public async Task<ConnectionTestResult> TestValueAsync(ConnectionServiceType serviceType, string value, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("/api/connections/test", new { serviceType, value }, JsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ConnectionTestResult>(JsonOptions, cancellationToken))!;
    }

    /// <summary>A 400 carries the reason (a blank or taken name, an incompatible operation/connection, ...) — surface it instead of a bare status code.</summary>
    private static async Task ThrowIfRejectedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemResponse>(JsonOptions, cancellationToken);
            throw new InvalidOperationException(problem?.Detail ?? "The server rejected the connection.");
        }
    }
}
