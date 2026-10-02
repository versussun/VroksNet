using System.Net.Http.Json;
using System.Text;
using VroksNet.E2ETests.Fixtures;
using static Microsoft.Playwright.Assertions;

namespace VroksNet.E2ETests;

/// <summary>
/// Provider mode in the Admin UI: the "Serve at real path" switch on an operation card persists and
/// shows where to point a service; a second spec declaring the same operation is refused — per
/// operation with the reason shown and the switch left off, and by "Serve all" as skipped. Paths are
/// GUID-suffixed so other tests' specs can't overlap, and provider mode is switched off again in a
/// finally so a failure can't leave anything served.
/// </summary>
[Collection("AppHost")]
public sealed class ProviderModeTests(AppHostFixture fixture) : PageTestBase(fixture)
{
    [Fact]
    public async Task ServeAtRealPath_Persists_AndAnOverlappingOperationIsRefused()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var operationKey = $"GET /e2e-provider-{suffix}/pets";
        var titleA = $"E2E Provider A {suffix}";
        var titleB = $"E2E Provider B {suffix}";
        var specIds = new List<Guid>();

        try
        {
            specIds.Add(await UploadAndOpenAsync(titleA, suffix));
            var serveSwitch = Card(operationKey).GetByRole(AriaRole.Switch, new LocatorGetByRoleOptions { Name = "Serve at real path" });
            await Expect(serveSwitch).Not.ToBeCheckedAsync();
            await Expect(Card(operationKey)).ToContainTextAsync($"http://localhost:7353/e2e-provider-{suffix}/pets");

            // Wait for the server to confirm before reloading, rather than racing the PUT.
            await Page.RunAndWaitForResponseAsync(() => serveSwitch.CheckAsync(), "**/provider-mode");
            await Page.ReloadAsync();
            await Expect(Card(operationKey).GetByRole(AriaRole.Switch, new LocatorGetByRoleOptions { Name = "Serve at real path" })).ToBeCheckedAsync();

            // A second spec with the same operation can't be served too — neither one by one…
            specIds.Add(await UploadAndOpenAsync(titleB, suffix));
            var otherCard = Card(operationKey);
            var otherSwitch = otherCard.GetByRole(AriaRole.Switch, new LocatorGetByRoleOptions { Name = "Serve at real path" });
            await otherSwitch.ClickAsync();
            await Expect(otherCard.GetByRole(AriaRole.Alert)).ToContainTextAsync(titleA);
            await Expect(otherSwitch).Not.ToBeCheckedAsync();

            // …nor via "Serve all", which reports it as skipped.
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Serve all at real paths" }).ClickAsync();
            await Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync("1 skipped");
            await Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync(titleA);

            // "Stop serving" on spec A frees the path again.
            await Page.GotoAsync($"/specifications/{specIds[0]}");
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Stop serving at real paths" }).ClickAsync();
            await Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync("anymore");
            await Expect(Card(operationKey).GetByRole(AriaRole.Switch, new LocatorGetByRoleOptions { Name = "Serve at real path" })).Not.ToBeCheckedAsync();
        }
        finally
        {
            using var api = new HttpClient { BaseAddress = Fixture.ApiServiceHttpAddress };
            foreach (var id in specIds)
            {
                await api.PutAsJsonAsync($"/api/specifications/{id}/provider-mode", new { enabled = false }, TestContext.Current.CancellationToken);
            }
        }
    }

    private ILocator Card(string operationKey) => Page.GetByRole(AriaRole.Region, new PageGetByRoleOptions { Name = operationKey, Exact = true });

    /// <summary>Uploads a one-operation spec and opens its detail page; returns the spec id (from the URL).</summary>
    private async Task<Guid> UploadAndOpenAsync(string title, string suffix)
    {
        await Page.GotoAsync("/specifications");
        await Page.Locator("input[type=file]").SetInputFilesAsync(new FilePayload
        {
            Name = "provider.yaml",
            MimeType = "application/yaml",
            Buffer = Encoding.UTF8.GetBytes($"""
                openapi: 3.0.3
                info:
                  title: "{title}"
                  version: "1.0.0"
                paths:
                  /e2e-provider-{suffix}/pets:
                    get:
                      responses:
                        "200":
                          description: OK
                """),
        });
        var row = Page.Locator("table tbody tr", new PageLocatorOptions { HasText = title });
        await row.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = title, Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Level = 1 })).ToHaveTextAsync(title);
        return Guid.Parse(Page.Url.TrimEnd('/').Split('/')[^1]);
    }
}
