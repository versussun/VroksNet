using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using VroksNet.E2ETests.Fixtures;
using static Microsoft.Playwright.Assertions;

namespace VroksNet.E2ETests;

/// <summary>
/// Drives the Test Suites page: building a suite from existing scenarios, running it, and reading
/// each scenario's result. The scenarios are set up over the API — their own UI is covered by
/// TestScenariosPageTests — and hit ApiService's own /health over plain http, so they pass anywhere.
/// </summary>
[Collection("AppHost")]
public sealed class TestSuitesPageTests(AppHostFixture fixture) : PageTestBase(fixture)
{
    [Fact]
    public async Task CreateAndRunASuite_ShowsEachScenariosResult()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var suiteName = $"E2E Suite {suffix}";
        var (first, second) = await CreateScenariosAsync(suffix);

        await Page.GotoAsync("/test-suites");
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "+ Add test suite" }).ClickAsync();
        var add = Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Add", Exact = true });
        await Page.GetByLabel("Name").FillAsync(suiteName);
        await Expect(add).ToBeDisabledAsync(); // no scenarios ticked yet
        await Page.GetByLabel(first).CheckAsync();
        await Page.GetByLabel(second).CheckAsync();
        await Page.GetByLabel("Run on startup").CheckAsync();
        await add.ClickAsync();

        var row = Page.Locator("table tbody tr", new PageLocatorOptions { HasText = suiteName });
        await Expect(row).ToContainTextAsync($"{first}, {second}");
        await Expect(row).ToContainTextAsync("on startup");
        await Expect(row).ToContainTextAsync("Never run");

        await row.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Run", Exact = true }).ClickAsync();
        await Expect(row).ToContainTextAsync("Passed");

        await row.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Runs" }).ClickAsync();
        var scenarios = Page.GetByRole(AriaRole.Table, new PageGetByRoleOptions { Name = "Scenarios of suite run" }).First;
        await Expect(scenarios.Locator("tr")).ToHaveCountAsync(2);
        await Expect(scenarios.Locator("tr").Nth(0)).ToContainTextAsync(first);
        await Expect(scenarios.Locator("tr").Nth(0)).ToContainTextAsync("Passed");
        await Expect(scenarios.Locator("tr").Nth(1)).ToContainTextAsync(second);
    }

    private async Task<(string First, string Second)> CreateScenariosAsync(string suffix)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var api = Fixture.App.CreateHttpClient("apiservice", "http");

        using var import = new HttpRequestMessage(HttpMethod.Post, "/api/specifications/openapi")
        {
            Content = new StringContent($$"""
                openapi: 3.0.3
                info: { title: "E2E Suite Health {{suffix}}", version: "1" }
                paths:
                  /health:
                    get:
                      responses:
                        "200": { description: OK }
                """, Encoding.UTF8, "text/plain")
        };
        var specificationId = (await (await api.SendAsync(import, cancellationToken)).Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!["id"]!.GetValue<Guid>();
        var endpointId = (await api.GetFromJsonAsync<JsonNode>($"/api/specifications/{specificationId}", cancellationToken))!["endpoints"]![0]!["id"]!.GetValue<Guid>();
        var connectionId = await PostIdAsync(api, "/api/connections", new { Name = $"E2E Suite Self {suffix}", ServiceType = "Http", Value = Fixture.ApiServiceHttpAddress.ToString() });

        var first = $"E2E Suite First {suffix}";
        var second = $"E2E Suite Second {suffix}";
        foreach (var name in new[] { first, second })
        {
            await PostIdAsync(api, "/api/test-scenarios", new { Name = name, SpecificationId = specificationId, MockEndpointId = endpointId, ConnectionId = connectionId });
        }

        return (first, second);
    }

    private static async Task<Guid> PostIdAsync(HttpClient api, string path, object body)
    {
        var response = await api.PostAsJsonAsync(path, body, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonNode>(TestContext.Current.CancellationToken))!["id"]!.GetValue<Guid>();
    }
}
