using System.Net.Http.Json;

namespace VroksNet.Web;

public sealed class SystemApiClient(HttpClient httpClient)
{
    public async Task<StorageStatus?> GetStorageStatusAsync(CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync<StorageStatus>("/api/system/storage", cancellationToken);

    public async Task<ProviderInfo?> GetProviderInfoAsync(CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync<ProviderInfo>("/api/system/provider", cancellationToken);
}
