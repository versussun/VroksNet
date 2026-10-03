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
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "+ Add connection" }).ClickAsync();
        await Page.GetByLabel("Name").FillAsync(connectionName);
        await Page.GetByLabel("URL").FillAsync("https://api.example.com");
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Add", Exact = true }).ClickAsync();
        await Expect(Page.Locator("table tbody tr", new PageLocatorOptions { HasText = connectionName })).ToBeVisibleAsync();

        // Create the scenario.
        await Page.GotoAsync("/test-scenarios");
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "+ Add test scenario" }).ClickAsync();
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
        await row.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Run", Exact = true }).ClickAsync();
        await Expect(row.Locator("span.badge").Last).ToBeVisibleAsync();

        // The "Last run" status is persisted server-side (RunTestScenarioHandler records it, and
        // it's retrievable via GET /api/test-scenarios/{id} independent of this browser session)
        // — a reload should still show it, not "Never run".
        await Page.ReloadAsync();
        var rowAfterReload = Page.Locator("table tbody tr", new PageLocatorOptions { HasText = scenarioName });
        await Expect(rowAfterReload).Not.ToContainTextAsync("Never run");
        await Expect(rowAfterReload.Locator("span.badge")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Run_ResponseNotMatchingSpec_ShowsContractViolation()
    {
        var suffix = Guid.NewGuid();
        var specTitle = $"E2E Contract Spec {suffix}";
        var connectionName = $"E2E Contract Connection {suffix}";
        var scenarioName = $"E2E Contract Scenario {suffix}";

        // ApiService's own GET /api/connections always answers 200 with a JSON array, so
        // declaring it as an object is a guaranteed, network-independent contract violation.
        await Page.GotoAsync("/specifications");
        await Page.Locator("input[type=file]").SetInputFilesAsync(new FilePayload
        {
            Name = "contract.yaml",
            MimeType = "application/yaml",
            Buffer = Encoding.UTF8.GetBytes($"""
                openapi: 3.0.3
                info:
                  title: "{specTitle}"
                  version: "1.0.0"
                paths:
                  /api/connections:
                    get:
                      responses:
                        "200":
                          description: OK
                          content:
                            application/json:
                              schema:
                                type: object
                """),
        });
        await Expect(Page.Locator("table tbody tr", new PageLocatorOptions { HasText = specTitle })).ToBeVisibleAsync();

        await Page.GotoAsync("/settings");
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "+ Add connection" }).ClickAsync();
        await Page.GetByLabel("Name").FillAsync(connectionName);
        await Page.GetByLabel("URL").FillAsync(Fixture.ApiServiceHttpAddress.ToString());
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Add", Exact = true }).ClickAsync();
        await Expect(Page.Locator("table tbody tr", new PageLocatorOptions { HasText = connectionName })).ToBeVisibleAsync();

        await Page.GotoAsync("/test-scenarios");
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "+ Add test scenario" }).ClickAsync();
        await Page.GetByLabel("Name").FillAsync(scenarioName);
        await Page.GetByLabel("Specification").SelectOptionAsync(new SelectOptionValue { Label = $"{specTitle} (OpenApi)" });
        await Expect(Page.Locator("#scenario-operation-select option")).ToHaveCountAsync(2);
        await Page.GetByLabel("Operation").SelectOptionAsync(new SelectOptionValue { Label = "GET /api/connections" });
        await Page.GetByLabel("Connection").SelectOptionAsync(new SelectOptionValue { Label = $"{connectionName} (Http)" });
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Add", Exact = true }).ClickAsync();

        var row = Page.Locator("table tbody tr", new PageLocatorOptions { HasText = scenarioName });
        await row.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Run", Exact = true }).ClickAsync();

        // Checked first so a failed send (which skips validation, so no contract badge renders)
        // fails here with the actual error text rather than as a missing-element timeout below.
        await Expect(row).ToContainTextAsync("doesn't match the spec");

        var contract = row.GetByRole(AriaRole.Status, new LocatorGetByRoleOptions { Name = "Contract check" });
        await Expect(contract).ToContainTextAsync("Contract violated");
        await Expect(contract.Locator("li")).Not.ToHaveCountAsync(0);
    }

    [Fact]
    public async Task ListenScenario_ForASendOperation_DefaultsToListen_AndReportsATimeout()
    {
        var suffix = Guid.NewGuid();
        var specTitle = $"E2E Listen Spec {suffix}";
        var connectionName = $"E2E Listen Broker {suffix}";
        // The scenario name deliberately avoids the word "Listen", so the badge assertion below
        // can't be satisfied by the name.
        var scenarioName = $"E2E Order Watcher {suffix}";
        var rabbitMqConnectionString = await Fixture.App.GetConnectionStringAsync("rabbitmq", TestContext.Current.CancellationToken);
        Assert.NotNull(rabbitMqConnectionString);

        await Page.GotoAsync("/specifications");
        await Page.Locator("#spec-kind-select").SelectOptionAsync("AsyncApi");
        await Page.Locator("input[type=file]").SetInputFilesAsync(new FilePayload
        {
            Name = "listen.yaml",
            MimeType = "application/yaml",
            Buffer = Encoding.UTF8.GetBytes($"""
                asyncapi: 3.0.0
                info:
                  title: "{specTitle}"
                  version: "1.0.0"
                channels:
                  orderCreated:
                    address: e2e.listen.{suffix:N}
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
        await Page.GetByLabel("Service type").SelectOptionAsync("RabbitMq");
        await Page.GetByLabel("Connection string").FillAsync(rabbitMqConnectionString);
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Add", Exact = true }).ClickAsync();
        await Expect(Page.Locator("table tbody tr", new PageLocatorOptions { HasText = connectionName })).ToBeVisibleAsync();

        await Page.GotoAsync("/test-scenarios");
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "+ Add test scenario" }).ClickAsync();
        await Page.GetByLabel("Name").FillAsync(scenarioName);
        await Page.GetByLabel("Specification").SelectOptionAsync(new SelectOptionValue { Label = $"{specTitle} (AsyncApi)" });
        await Expect(Page.Locator("#scenario-operation-select option")).ToHaveCountAsync(2);
        await Page.GetByLabel("Operation").SelectOptionAsync(new SelectOptionValue { Label = $"e2e.listen.{suffix:N}:send" });

        // A "send" operation is one the service publishes — the form starts in Listen mode.
        await Expect(Page.GetByLabel("Mode")).ToHaveValueAsync("Listen");
        await Page.GetByLabel("Connection").SelectOptionAsync(new SelectOptionValue { Label = $"{connectionName} (RabbitMq)" });
        await Expect(Page.GetByLabel("Exchange")).ToBeVisibleAsync();
        await Expect(Page.GetByLabel("Payload (optional override)")).Not.ToBeVisibleAsync();

        // Out-of-range timeouts are caught before saving (the limit is 30 minutes).
        await Page.GetByLabel("Wait up to (seconds)").FillAsync("1801");
        await Expect(Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Add", Exact = true })).ToBeDisabledAsync();
        await Page.GetByLabel("Wait up to (seconds)").FillAsync("1");
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Add", Exact = true }).ClickAsync();

        var row = Page.Locator("table tbody tr", new PageLocatorOptions { HasText = scenarioName });
        await Expect(row.GetByText("Listen", new LocatorGetByTextOptions { Exact = true })).ToBeVisibleAsync();

        // Nothing publishes on the channel, so the run waits its 1s and fails with a timeout.
        await row.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Run", Exact = true }).ClickAsync();
        await Expect(row).ToContainTextAsync("No message");
    }

    [Fact]
    public async Task RunInBackground_ShowsTheResult_AndTheRunAppearsInTheHistory()
    {
        var suffix = Guid.NewGuid();
        var specTitle = $"E2E Background Petstore {suffix}";
        var connectionName = $"E2E Background Connection {suffix}";
        var scenarioName = $"E2E Background Scenario {suffix}";

        await Page.GotoAsync("/specifications");
        await Page.Locator("input[type=file]").SetInputFilesAsync(new FilePayload
        {
            Name = "petstore.yaml",
            MimeType = "application/yaml",
            Buffer = Encoding.UTF8.GetBytes(BuildPetstoreYaml(specTitle)),
        });
        await Expect(Page.Locator("table tbody tr", new PageLocatorOptions { HasText = specTitle })).ToBeVisibleAsync();

        // The run goes to ApiService itself, so it finishes quickly and deterministically, whatever
        // its result (/pets isn't one of ApiService's routes).
        await Page.GotoAsync("/settings");
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "+ Add connection" }).ClickAsync();
        await Page.GetByLabel("Name").FillAsync(connectionName);
        await Page.GetByLabel("URL").FillAsync(Fixture.ApiServiceHttpAddress.ToString());
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Add", Exact = true }).ClickAsync();
        await Expect(Page.Locator("table tbody tr", new PageLocatorOptions { HasText = connectionName })).ToBeVisibleAsync();

        await Page.GotoAsync("/test-scenarios");
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "+ Add test scenario" }).ClickAsync();
        await Page.GetByLabel("Name").FillAsync(scenarioName);
        await Page.GetByLabel("Specification").SelectOptionAsync(new SelectOptionValue { Label = $"{specTitle} (OpenApi)" });
        await Expect(Page.Locator("#scenario-operation-select option")).ToHaveCountAsync(2);
        await Page.GetByLabel("Operation").SelectOptionAsync(new SelectOptionValue { Label = "GET /pets" });
        await Page.GetByLabel("Connection").SelectOptionAsync(new SelectOptionValue { Label = $"{connectionName} (Http)" });
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Add", Exact = true }).ClickAsync();

        var row = Page.Locator("table tbody tr", new PageLocatorOptions { HasText = scenarioName });
        await row.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Run in background" }).ClickAsync();

        // The run's result replaces the in-progress button once the worker has finished it.
        await Expect(row.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Run in background" })).ToBeVisibleAsync();
        await Expect(row.Locator("span.badge").Last).ToBeVisibleAsync();
        await Expect(row).Not.ToContainTextAsync("Never run");

        await row.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "History" }).ClickAsync();
        var history = Page.GetByRole(AriaRole.Table, new PageGetByRoleOptions { Name = $"Run history of {scenarioName}" });
        await Expect(history.Locator("tbody tr")).ToHaveCountAsync(1);
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
