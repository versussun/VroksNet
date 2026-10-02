using System.Text;
using VroksNet.E2ETests.Fixtures;
using static Microsoft.Playwright.Assertions;

namespace VroksNet.E2ETests;

/// <summary>
/// Drives the real Specifications pages through a browser — upload → list → detail → the detail
/// page's live "Send" mock try-it — the UI counterpart to VroksNet.IntegrationTests' HTTP-level
/// /api/specifications coverage. Uses a GUID-suffixed title so this run's row is unambiguous
/// among whatever the persisted database already has from earlier runs (see .claude/CLAUDE.md
/// "Testing").
/// </summary>
[Collection("AppHost")]
public sealed class SpecificationsPageTests(AppHostFixture fixture) : PageTestBase(fixture)
{
    [Fact]
    public async Task UploadSpec_Then_ListAndDetailShowIt_AndMockTryItWorks()
    {
        var title = $"E2E Test Petstore {Guid.NewGuid()}";
        await Page.GotoAsync("/specifications");

        // Upload
        await Page.Locator("input[type=file]").SetInputFilesAsync(new FilePayload
        {
            Name = "petstore.yaml",
            MimeType = "application/yaml",
            Buffer = Encoding.UTF8.GetBytes(BuildPetstoreYaml(title)),
        });

        var row = Page.Locator("table tbody tr", new PageLocatorOptions { HasText = title });
        await Expect(row).ToBeVisibleAsync();
        await Expect(row.Locator("td").Nth(1)).ToHaveTextAsync("OpenApi");
        await Expect(row.Locator("td").Nth(2)).ToHaveTextAsync("1");

        // View raw source — the panel must show the uploaded YAML itself, not a literal
        // "_viewingRawContent" (a string component parameter passed without "@" is taken verbatim).
        await row.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "View" }).ClickAsync();
        var sourcePanel = Page.GetByRole(AriaRole.Complementary, new PageGetByRoleOptions { Name = "Specification source" });
        await Expect(sourcePanel.Locator("pre")).ToContainTextAsync("openapi: 3.0.3");
        await Expect(sourcePanel.Locator("pre")).ToContainTextAsync(title);
        await sourcePanel.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Close" }).ClickAsync();

        // Detail
        await row.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = title, Exact = true }).ClickAsync();
        await Expect(Page.Locator("h1")).ToHaveTextAsync(title);

        var endpointCard = Page.Locator(".card", new PageLocatorOptions { HasText = "GET /pets" });
        await Expect(endpointCard.Locator("span.badge")).ToHaveTextAsync("Enabled");

        // Live mock try-it
        await endpointCard.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Send" }).ClickAsync();
        await Expect(endpointCard.Locator("span.badge").Last).ToHaveTextAsync("200");
        await Expect(endpointCard.Locator("pre code").Last).ToContainTextAsync("Fido");

        // Turning the mock off is saved, not just shown. (A 404 isn't asserted: another spec in the
        // same run may also declare GET /pets and answer it.)
        var enabledSwitch = endpointCard.GetByRole(AriaRole.Switch, new LocatorGetByRoleOptions { Name = "Mock enabled" });
        await enabledSwitch.UncheckAsync();
        await Expect(endpointCard.Locator("span.badge").First).ToHaveTextAsync("Disabled");
        await Page.ReloadAsync();
        await Expect(enabledSwitch).Not.ToBeCheckedAsync();
        await enabledSwitch.CheckAsync();
        await Expect(endpointCard.Locator("span.badge").First).ToHaveTextAsync("Enabled");
    }

    [Fact]
    public async Task UploadAsyncApiSpec_WithKindSelector_ListsItButSkipsLiveTryIt()
    {
        var title = $"E2E Test Orders {Guid.NewGuid()}";
        await Page.GotoAsync("/specifications");

        // Switch the kind selector before uploading — this is the UI wiring under test.
        await Page.Locator("#spec-kind-select").SelectOptionAsync("AsyncApi");
        await Page.Locator("input[type=file]").SetInputFilesAsync(new FilePayload
        {
            Name = "orders.yaml",
            MimeType = "application/yaml",
            Buffer = Encoding.UTF8.GetBytes(BuildOrdersAsyncApiYaml(title)),
        });

        var row = Page.Locator("table tbody tr", new PageLocatorOptions { HasText = title });
        await Expect(row).ToBeVisibleAsync();
        await Expect(row.Locator("td").Nth(1)).ToHaveTextAsync("AsyncApi");
        await Expect(row.Locator("td").Nth(2)).ToHaveTextAsync("1");

        // Detail — the card shows the operation but no live "Send" try-it: AsyncAPI operation
        // keys aren't invokable through the HTTP-shaped /mock/{**path} route (see
        // .claude/CLAUDE.md "Infrastructure notes").
        await row.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = title, Exact = true }).ClickAsync();
        var endpointCard = Page.Locator(".card", new PageLocatorOptions { HasText = "orders.created:send" });
        await Expect(endpointCard).ToBeVisibleAsync();
        await Expect(endpointCard.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Send" })).Not.ToBeVisibleAsync();
        await Expect(endpointCard).ToContainTextAsync("aren't invoked here");
        await Expect(endpointCard.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "Publishers" })).ToHaveAttributeAsync("href", "publishers");
    }

    private static string BuildOrdersAsyncApiYaml(string title) => $"""
        asyncapi: 3.0.0
        info:
          title: "{title}"
          version: "1.0.0"
        channels:
          orderCreated:
            address: orders.created
            messages:
              orderCreated:
                $ref: "#/components/messages/OrderCreated"
        operations:
          publishOrderCreated:
            action: send
            channel:
              $ref: "#/channels/orderCreated"
            messages:
              - $ref: "#/channels/orderCreated/messages/orderCreated"
        components:
          messages:
            OrderCreated:
              payload:
                type: object
              examples:
                - name: OrderCreatedExample
                  payload:
                    orderId: "ord_1"
        """;

    private static string BuildPetstoreYaml(string title) => $"""
        openapi: 3.0.3
        info:
          title: "{title}"
          version: "1.0.0"
        paths:
          /pets:
            get:
              operationId: listPets
              summary: List all pets
              responses:
                "200":
                  description: A page of pets
                  content:
                    application/json:
                      schema:
                        type: array
                        items:
                          type: object
                      example:
                        - id: 1
                          name: Fido
        """;
}
