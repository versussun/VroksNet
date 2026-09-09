using VroksNet.E2ETests.Fixtures;
using static Microsoft.Playwright.Assertions;

namespace VroksNet.E2ETests;

/// <summary>
/// Drives the real Settings page through a browser — the UI counterpart to
/// VroksNet.IntegrationTests' HTTP-level /api/connections coverage. Uses a GUID-suffixed name:
/// Connection.Name has a unique index (see .claude/CLAUDE.md "Infrastructure notes") and the
/// backing SQLite file persists across test runs.
/// </summary>
[Collection("AppHost")]
public sealed class SettingsPageTests(AppHostFixture fixture) : PageTestBase(fixture)
{
    [Fact]
    public async Task AddEditDeleteConnection_RoundTripsThroughTheUi()
    {
        var name = $"E2E Test Connection {Guid.NewGuid()}";
        await Page.GotoAsync("/settings");

        // Add
        await Page.GetByLabel("Name").FillAsync(name);
        await Page.GetByLabel("URL").FillAsync("https://api.example.com");
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Add", Exact = true }).ClickAsync();

        var row = Page.Locator("table tbody tr", new PageLocatorOptions { HasText = name });
        await Expect(row).ToBeVisibleAsync();
        await Expect(row).ToContainTextAsync("Http");

        // Test — verifies the Test button is wired end to end through the real
        // /api/connections/{id}/test endpoint. Doesn't assert success/failure, since that
        // depends on real outbound network reachability of the (external) target — only that a
        // result badge appears.
        await row.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Test" }).ClickAsync();
        await Expect(row.Locator("span.badge")).ToBeVisibleAsync();

        // Edit — switch its service type, leaving the connection string as-is
        await row.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Edit" }).ClickAsync();
        await Page.GetByLabel("Service type").SelectOptionAsync("RabbitMq");
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Save changes" }).ClickAsync();
        await Expect(row).ToContainTextAsync("RabbitMq");

        // Delete — auto-accept the browser's confirm() dialog
        Page.Dialog += async (_, dialog) => await dialog.AcceptAsync();
        await row.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Delete" }).ClickAsync();
        await Expect(row).Not.ToBeVisibleAsync();
    }
}
