using System.Net.Http.Json;
using System.Text.Json.Nodes;
using VroksNet.IntegrationTests.Fixtures;

namespace VroksNet.IntegrationTests;

/// <summary>
/// <c>GET /api/system/info</c> (docs/container-contract.md §6, step A4): the shape the Aspire
/// hosting package reads. The AppHost graph has no provisioning directory and no
/// <c>Provisioning__Connections__*</c>, so provisioning reports NotConfigured.
/// </summary>
[Collection("AppHost")]
public sealed class SystemInfoApiTests(AppHostFixture fixture)
{
    [Fact]
    public async Task Info_ReportsVersionContractAndProvisioning()
    {
        var info = await fixture.ApiServiceClient.GetFromJsonAsync<JsonNode>("/api/system/info", TestContext.Current.CancellationToken);

        Assert.False(string.IsNullOrWhiteSpace(info!["version"]!.GetValue<string>()));
        Assert.DoesNotContain("+", info["version"]!.GetValue<string>());
        Assert.Equal(1, info["contractVersion"]!.GetValue<int>());

        var provisioning = info["provisioning"]!;
        Assert.Equal("NotConfigured", provisioning["status"]!.GetValue<string>());
        Assert.Equal(0, provisioning["counts"]!["specifications"]!.GetValue<int>());
        Assert.Empty(provisioning["errors"]!.AsArray());
    }
}
