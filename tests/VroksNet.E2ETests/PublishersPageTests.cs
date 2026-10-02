using System.Text;
using VroksNet.E2ETests.Fixtures;
using static Microsoft.Playwright.Assertions;

namespace VroksNet.E2ETests;

/// <summary>
/// Drives the Publishers page through a browser: an AsyncAPI spec and a NATS connection (to the
/// AppHost's own container) are set up through their pages, then a publisher is created, published
/// on demand, and started/stopped. Created stopped, so the background worker doesn't publish it in
/// the meantime; "Publish now" works either way.
/// </summary>
[Collection("AppHost")]
public sealed class PublishersPageTests(AppHostFixture fixture) : PageTestBase(fixture)
{
    [Fact]
    public async Task CreatePublisher_PublishNow_ThenStartAndStop()
    {
        var suffix = Guid.NewGuid();
        var specTitle = $"E2E Publisher Spec {suffix}";
        var connectionName = $"E2E Publisher Broker {suffix}";
        var publisherName = $"E2E Order Feed {suffix}";
        var natsConnectionString = await Fixture.App.GetConnectionStringAsync("nats", TestContext.Current.CancellationToken);
        Assert.NotNull(natsConnectionString);

        await Page.GotoAsync("/specifications");
        await Page.Locator("#spec-kind-select").SelectOptionAsync("AsyncApi");
        await Page.Locator("input[type=file]").SetInputFilesAsync(new FilePayload
        {
            Name = "publisher.yaml",
            MimeType = "application/yaml",
            Buffer = Encoding.UTF8.GetBytes($"""
                asyncapi: 3.0.0
                info:
                  title: "{specTitle}"
                  version: "1.0.0"
                channels:
                  orderCreated:
                    address: e2e.publisher.{suffix:N}
                operations:
                  publishOrderCreated:
                    action: send
                    channel:
                      $ref: "#/channels/orderCreated"
                """),
        });
        await Expect(Page.Locator("table tbody tr", new PageLocatorOptions { HasText = specTitle })).ToBeVisibleAsync();

        await Page.GotoAsync("/settings");
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "+ Add connection" }).ClickAsync();
        await Page.GetByLabel("Name").FillAsync(connectionName);
        await Page.GetByLabel("Service type").SelectOptionAsync("Nats");
        await Page.GetByLabel("Connection string").FillAsync(natsConnectionString);
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Add", Exact = true }).ClickAsync();
        await Expect(Page.Locator("table tbody tr", new PageLocatorOptions { HasText = connectionName })).ToBeVisibleAsync();

        await Page.GotoAsync("/publishers");
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "+ Add publisher" }).ClickAsync();
        await Page.GetByLabel("Name").FillAsync(publisherName);
        await Page.GetByLabel("Specification").SelectOptionAsync(new SelectOptionValue { Label = specTitle });
        await Expect(Page.Locator("#publisher-operation-select option")).ToHaveCountAsync(2);
        await Page.GetByLabel("Operation").SelectOptionAsync(new SelectOptionValue { Label = $"e2e.publisher.{suffix:N}:send" });
        await Page.GetByLabel("Connection").SelectOptionAsync(new SelectOptionValue { Label = $"{connectionName} (Nats)" });
        await Expect(Page.GetByLabel("Exchange")).Not.ToBeVisibleAsync(); // RabbitMQ only
        await Page.GetByLabel("Payload (optional override)").FillAsync("""{"orderId":"{{uuid}}"}""");

        // Out-of-range intervals are caught before saving.
        await Page.GetByLabel("Every (seconds)").FillAsync("0");
        await Expect(Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Add", Exact = true })).ToBeDisabledAsync();
        await Page.GetByLabel("Every (seconds)").FillAsync("3600");
        await Page.GetByLabel("Start publishing right away").UncheckAsync();
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Add", Exact = true }).ClickAsync();

        var row = Page.Locator("table tbody tr", new PageLocatorOptions { HasText = publisherName });
        await Expect(row).ToContainTextAsync("1h");
        await Expect(row).ToContainTextAsync("Never");

        await row.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Publish now" }).ClickAsync();
        var result = row.GetByRole(AriaRole.Status, new LocatorGetByRoleOptions { Name = $"Publish result for {publisherName}" });
        await Expect(result).ToContainTextAsync("OK");
        await Expect(result.Locator("pre")).Not.ToContainTextAsync("{{uuid}}"); // rendered

        var running = row.GetByRole(AriaRole.Switch, new LocatorGetByRoleOptions { Name = $"Publish {publisherName} on schedule" });
        await Expect(running).Not.ToBeCheckedAsync();
        await running.CheckAsync();
        await Page.ReloadAsync();
        await Expect(running).ToBeCheckedAsync();
        await running.UncheckAsync();
        await Page.ReloadAsync();
        await Expect(running).Not.ToBeCheckedAsync();
    }
}
