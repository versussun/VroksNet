using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VroksNet.Web;

public sealed class PublisherApiClient(HttpClient httpClient)
{
    // Matches ApiService's ConfigureHttpJsonOptions — see SpecificationApiClient for why this is needed.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<PublisherSummary[]> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var publishers = await httpClient.GetFromJsonAsync<PublisherSummary[]>("/api/publishers", JsonOptions, cancellationToken);
        return publishers ?? [];
    }

    /// <exception cref="InvalidOperationException">The server rejected the form (400) — its reason is the message.</exception>
    public async Task<Guid> CreateAsync(PublisherForm form, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("/api/publishers", form, JsonOptions, cancellationToken);
        await ThrowIfRejectedAsync(response, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CreatePublisherResult>(JsonOptions, cancellationToken);
        return result!.Id;
    }

    /// <summary>Returns false if no publisher with that id exists.</summary>
    /// <exception cref="InvalidOperationException">The server rejected the form (400) — its reason is the message.</exception>
    public async Task<bool> UpdateAsync(Guid id, PublisherForm form, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PutAsJsonAsync($"/api/publishers/{id}", form, JsonOptions, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        await ThrowIfRejectedAsync(response, cancellationToken);
        response.EnsureSuccessStatusCode();
        return true;
    }

    /// <summary>Returns false if no publisher with that id exists.</summary>
    public async Task<bool> SetEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PutAsJsonAsync($"/api/publishers/{id}/enabled", new { enabled }, JsonOptions, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        return true;
    }

    /// <summary>Returns false if no publisher with that id exists.</summary>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.DeleteAsync($"/api/publishers/{id}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        return true;
    }

    /// <summary>Returns null if no publisher with that id exists.</summary>
    public async Task<PublishResult?> PublishNowAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsync($"/api/publishers/{id}/publish", null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PublishResult>(JsonOptions, cancellationToken);
    }

    private static async Task ThrowIfRejectedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemResponse>(JsonOptions, cancellationToken);
            throw new InvalidOperationException(problem?.Detail ?? "The server rejected the publisher.");
        }
    }
}
