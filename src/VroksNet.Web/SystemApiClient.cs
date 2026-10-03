using System.Net.Http.Json;

namespace VroksNet.Web;

public sealed class SystemApiClient(HttpClient httpClient)
{
    public async Task<StorageStatus?> GetStorageStatusAsync(CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync<StorageStatus>("/api/system/storage", cancellationToken);

    public async Task<ProviderInfo?> GetProviderInfoAsync(CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync<ProviderInfo>("/api/system/provider", cancellationToken);

    public async Task<ConnectionTypeInfo[]> GetConnectionTypesAsync(CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync<ConnectionTypeInfo[]>("/api/system/connection-types", cancellationToken) ?? [];

    /// <summary>
    /// Where the configuration export downloads from — an absolute URL, since the API may be on
    /// another origin (dev). The server answers with an attachment, so a plain link downloads it.
    /// </summary>
    public string ExportUrl(bool inlineValues)
        => new Uri(httpClient.BaseAddress!, $"/api/provisioning/export{(inlineValues ? "?inlineValues=true" : string.Empty)}").ToString();
}
