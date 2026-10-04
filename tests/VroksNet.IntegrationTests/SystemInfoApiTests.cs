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

    /// <summary><c>GET /api/system/connection-types</c> (ADR 0003): what the Admin UI builds its type list and filters from.</summary>
    [Fact]
    public async Task ConnectionTypes_DescribeEveryType()
    {
        var types = await fixture.ApiServiceClient.GetFromJsonAsync<JsonArray>("/api/system/connection-types", TestContext.Current.CancellationToken);

        Assert.NotNull(types);
        Assert.Equal(["Http", "RabbitMq", "Nats", "Kafka", "Mqtt", "Redis", "ServiceBus", "Sqs", "Sns"], types.Select(type => type!["type"]!.GetValue<string>()));
        var http = types[0]!;
        Assert.Equal("HTTP", http["displayName"]!.GetValue<string>());
        Assert.True(http["isHttp"]!.GetValue<bool>());
        Assert.False(http["canListen"]!.GetValue<bool>());
        Assert.False(string.IsNullOrWhiteSpace(http["listenNote"]!.GetValue<string>()));
        // Every broker can be listened on except SQS, whose queues are Send only — and it says why.
        Assert.Equal(["Http", "Sqs"], types.Where(type => !type!["canListen"]!.GetValue<bool>()).Select(type => type!["type"]!.GetValue<string>()));
        Assert.Contains("compete for its messages", types.Single(type => type!["type"]!.GetValue<string>() == "Sqs")!["listenNote"]!.GetValue<string>());
    }
}
