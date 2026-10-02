using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VroksNet.Web;

public sealed class CallRecordApiClient(HttpClient httpClient)
{
    // Matches ApiService's ConfigureHttpJsonOptions — see SpecificationApiClient for why this is needed.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>One page of the history, newest first; pass the previous page's <see cref="CallRecordPage.NextCursor"/> as <paramref name="cursor"/> to continue.</summary>
    public async Task<CallRecordPage> GetPageAsync(
        Guid? specificationId,
        CallDirection? direction,
        bool? contractValid,
        string? cursor,
        CancellationToken cancellationToken = default)
    {
        var query = new List<string>();
        if (specificationId is { } id)
        {
            query.Add($"specificationId={id}");
        }

        if (direction is { } value)
        {
            query.Add($"direction={value}");
        }

        if (contractValid is { } valid)
        {
            query.Add($"contractValid={(valid ? "true" : "false")}");
        }

        if (cursor is not null)
        {
            query.Add($"cursor={Uri.EscapeDataString(cursor)}");
        }

        var url = query.Count == 0 ? "/api/call-records" : $"/api/call-records?{string.Join('&', query)}";
        var page = await httpClient.GetFromJsonAsync<CallRecordPage>(url, JsonOptions, cancellationToken);
        return page ?? new CallRecordPage([], null);
    }

    /// <summary>One record's request/response bodies. Returns null if no record with that id exists (e.g. the history was cleared meanwhile).</summary>
    public async Task<CallRecordDetails?> GetDetailsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync($"/api/call-records/{id}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CallRecordDetails>(JsonOptions, cancellationToken);
    }

    /// <returns>How many records were deleted.</returns>
    public async Task<int> ClearAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.DeleteAsync("/api/call-records", cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ClearCallRecordsResult>(JsonOptions, cancellationToken);
        return result?.Deleted ?? 0;
    }
}
