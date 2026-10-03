using System.Text.RegularExpressions;
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

        // Add — the form lives in a slide-out panel opened from the page header
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "+ Add connection" }).ClickAsync();
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

    [Fact]
    public async Task AddingAConnectionWithATakenName_ShowsWhy()
    {
        var name = $"E2E Duplicate Connection {Guid.NewGuid()}";
        await Page.GotoAsync("/settings");

        for (var attempt = 0; attempt < 2; attempt++)
        {
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "+ Add connection" }).ClickAsync();
            await Page.GetByLabel("Name").FillAsync(name);
            await Page.GetByLabel("URL").FillAsync("https://api.example.com");
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Add", Exact = true }).ClickAsync();
            if (attempt == 0)
            {
                await Expect(Page.Locator("table tbody tr", new PageLocatorOptions { HasText = name })).ToBeVisibleAsync();
            }
        }

        await Expect(Page.GetByText($"A connection named \"{name}\" already exists.")).ToBeVisibleAsync();
        await Expect(Page.Locator("table tbody tr", new PageLocatorOptions { HasText = name })).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task ProvisionedConnection_CarriesTheBadge_AndUiCreatedOnesDont()
    {
        var name = $"E2E Unprovisioned Connection {Guid.NewGuid()}";
        await Page.GotoAsync("/settings");
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "+ Add connection" }).ClickAsync();
        await Page.GetByLabel("Name").FillAsync(name);
        await Page.GetByLabel("URL").FillAsync("https://api.example.com");
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Add", Exact = true }).ClickAsync();

        var provisioned = Page.Locator("table tbody tr", new PageLocatorOptions { HasText = AppHostFixture.ProvisionedConnectionName });
        await Expect(provisioned.GetByText("Provisioned", new LocatorGetByTextOptions { Exact = true })).ToBeVisibleAsync();
        await Expect(provisioned.GetByText("Provisioned", new LocatorGetByTextOptions { Exact = true })).ToHaveAttributeAsync("title", new Regex("^Managed by provisioning"));

        var created = Page.Locator("table tbody tr", new PageLocatorOptions { HasText = name });
        await Expect(created).ToBeVisibleAsync();
        await Expect(created.GetByText("Provisioned", new LocatorGetByTextOptions { Exact = true })).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Export_DownloadsAProvisioningZip_WithoutConnectionValues()
    {
        await Page.GotoAsync("/settings");

        var download = await Page.RunAndWaitForDownloadAsync(() =>
            Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Export", Exact = true }).ClickAsync());

        Assert.Equal("vroksnet-provisioning.zip", download.SuggestedFilename);
        // Playwright's download stream only reads asynchronously; ZipArchive reads synchronously.
        await using var stream = await download.CreateReadStreamAsync();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, TestContext.Current.CancellationToken);
        buffer.Position = 0;
        using var zip = new System.IO.Compression.ZipArchive(buffer);
        using var manifest = new StreamReader(zip.GetEntry("vroksnet.yaml")!.Open());
        var yaml = await manifest.ReadToEndAsync(TestContext.Current.CancellationToken);
        // The test graph's provisioned connection, exported as a variable rather than its value.
        Assert.Contains($"name: \"{AppHostFixture.ProvisionedConnectionName}\"", yaml);
        Assert.Contains($"valueFrom: \"ConnectionStrings:{AppHostFixture.ProvisionedConnectionName}\"", yaml);
        Assert.DoesNotContain("http://provisioned.invalid", yaml);
    }
}
