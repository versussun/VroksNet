using System.Text;
using VroksNet.E2ETests.Fixtures;
using static Microsoft.Playwright.Assertions;

namespace VroksNet.E2ETests;

/// <summary>
/// Drives the Call History page: a mock call made from the Specifications page's own "Send"
/// try-it shows up in the history (filtered to its spec), its details expand, and "Clear history"
/// empties it. The spec's path is GUID-suffixed so the mock can't match another test's operation.
/// </summary>
[Collection("AppHost")]
public sealed class CallHistoryPageTests(AppHostFixture fixture) : PageTestBase(fixture)
{
    [Fact]
    public async Task MockCall_ShowsUpWithDetails_ThenClearHistoryEmptiesIt()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var title = $"E2E History Spec {suffix}";
        var operationKey = $"GET /history-{suffix}/pets";

        await Page.GotoAsync("/specifications");
        await Page.Locator("input[type=file]").SetInputFilesAsync(new FilePayload
        {
            Name = "history.yaml",
            MimeType = "application/yaml",
            Buffer = Encoding.UTF8.GetBytes($"""
                openapi: 3.0.3
                info:
                  title: "{title}"
                  version: "1.0.0"
                paths:
                  /history-{suffix}/pets:
                    get:
                      responses:
                        "200":
                          description: OK
                          content:
                            application/json:
                              example:
                                - name: Fido
                """),
        });
        var specRow = Page.Locator("table tbody tr", new PageLocatorOptions { HasText = title });
        await specRow.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = title, Exact = true }).ClickAsync();

        // Make one mock call through the UI's own try-it.
        var card = Page.Locator(".card", new PageLocatorOptions { HasText = operationKey });
        await card.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Send" }).ClickAsync();
        await Expect(card.Locator("span.badge").Last).ToHaveTextAsync("200");

        await Page.GotoAsync("/call-history");
        await Page.GetByLabel("Specification").SelectOptionAsync(new SelectOptionValue { Label = title });

        var row = Page.Locator("table tbody tr", new PageLocatorOptions { HasText = operationKey });
        await Expect(row).ToHaveCountAsync(1);
        await Expect(row).ToContainTextAsync("Mock call (in)");
        await Expect(row).ToContainTextAsync("200");

        var detailsButton = row.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Details" });
        await detailsButton.ClickAsync();
        await Expect(detailsButton).ToHaveAttributeAsync("aria-expanded", "true");
        var details = Page.GetByRole(AriaRole.Region, new PageGetByRoleOptions { Name = "Call details" });
        await Expect(details).ToContainTextAsync($"GET /mock/history-{suffix}/pets");
        await Expect(details).ToContainTextAsync("Fido");

        // Clears the *whole* history, other tests' records included. Safe today: the "AppHost"
        // collection runs sequentially and no other E2E test reads the history — any future one
        // that does must not rely on records created before this test runs.
        Page.Dialog += async (_, dialog) => await dialog.AcceptAsync();
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Clear history" }).ClickAsync();
        // The spec filter is still selected, so the empty state says so.
        await Expect(Page.GetByText("No calls match the selected filters.")).ToBeVisibleAsync();
        await Page.GetByLabel("Specification").SelectOptionAsync(new SelectOptionValue { Label = "All specifications" });
        await Expect(Page.GetByText("No calls recorded yet.")).ToBeVisibleAsync();
    }
}
