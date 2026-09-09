using System.Text;
using VroksNet.E2ETests.Fixtures;
using static Microsoft.Playwright.Assertions;

namespace VroksNet.E2ETests;

/// <summary>
/// Drives the real Test Scenarios page through a browser — composing the Specifications and
/// Settings pages' own upload/add flows (already covered individually by
/// SpecificationsPageTests/SettingsPageTests) to get a specification and a connection to pick
/// from, then creating and running a scenario against them. Doesn't assert the Run result's
/// success/failure — like SettingsPageTests' Test button, "https://api.example.com" is a real
/// external host, so only that a result badge appears is asserted (non-flaky regardless of
/// outbound network conditions).
/// </summary>
[Collection("AppHost")]
public sealed class TestScenariosPageTests(AppHostFixture fixture) : PageTestBase(fixture)
{
    [Fact]
    public async Task CreateScenario_FromUploadedSpecAndConnection_ThenRun()
    {
        var suffix = Guid.NewGuid();
        var specTitle = $"E2E Scenario Petstore {suffix}";
        var connectionName = $"E2E Scenario Connection {suffix}";
        var scenarioName = $"E2E Test Scenario {suffix}";

        // Upload a specification to pick an operation from.
        await Page.GotoAsync("/specifications");
        await Page.Locator("input[type=file]").SetInputFilesAsync(new FilePayload
        {
            Name = "petstore.yaml",
            MimeType = "application/yaml",
            Buffer = Encoding.UTF8.GetBytes(BuildPetstoreYaml(specTitle)),
        });
        await Expect(Page.Locator("table tbody tr", new PageLocatorOptions { HasText = specTitle })).ToBeVisibleAsync();

        // Add a connection to send it through.
        await Page.GotoAsync("/settings");
        await Page.GetByLabel("Name").FillAsync(connectionName);
        await Page.GetByLabel("URL").FillAsync("https://api.example.com");
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Add", Exact = true }).ClickAsync();
        await Expect(Page.Locator("table tbody tr", new PageLocatorOptions { HasText = connectionName })).ToBeVisibleAsync();

        // Create the scenario.
        await Page.GotoAsync("/test-scenarios");
        await Page.GetByLabel("Name").FillAsync(scenarioName);
        await Page.GetByLabel("Specification").SelectOptionAsync(new SelectOptionValue { Label = $"{specTitle} (OpenApi)" });

        // The operation dropdown populates from a follow-up fetch after picking the spec.
        await Expect(Page.Locator("#scenario-operation-select option")).ToHaveCountAsync(2);
        await Page.GetByLabel("Operation").SelectOptionAsync(new SelectOptionValue { Label = "GET /pets" });
        await Page.GetByLabel("Connection").SelectOptionAsync(new SelectOptionValue { Label = $"{connectionName} (Http)" });

        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Add", Exact = true }).ClickAsync();

        var row = Page.Locator("table tbody tr", new PageLocatorOptions { HasText = scenarioName });
        await Expect(row).ToBeVisibleAsync();
        await Expect(row).ToContainTextAsync(specTitle);
        await Expect(row).ToContainTextAsync("GET /pets");
        await Expect(row).ToContainTextAsync(connectionName);

        // Run it — only that a result appears is asserted (see class doc comment). Two badges now
        // exist in the row (the persisted "Last run" column, and this run's own ephemeral result
        // detail after the action buttons) — .Last is the latter.
        await row.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Run" }).ClickAsync();
        await Expect(row.Locator("span.badge").Last).ToBeVisibleAsync();

        // The "Last run" status is persisted server-side (RunTestScenarioHandler records it, and
        // it's retrievable via GET /api/test-scenarios/{id} independent of this browser session)
        // — a reload should still show it, not "Never run".
        await Page.ReloadAsync();
        var rowAfterReload = Page.Locator("table tbody tr", new PageLocatorOptions { HasText = scenarioName });
        await Expect(rowAfterReload).Not.ToContainTextAsync("Never run");
        await Expect(rowAfterReload.Locator("span.badge")).ToBeVisibleAsync();
    }

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
