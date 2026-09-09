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

        // Detail
        await row.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = title, Exact = true }).ClickAsync();
        await Expect(Page.Locator("h1")).ToHaveTextAsync(title);

        var endpointCard = Page.Locator(".card", new PageLocatorOptions { HasText = "GET /pets" });
        await Expect(endpointCard.Locator("span.badge")).ToHaveTextAsync("Enabled");

        // Live mock try-it
        await endpointCard.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Send" }).ClickAsync();
        await Expect(endpointCard.Locator("span.badge").Last).ToHaveTextAsync("200");
        await Expect(endpointCard.Locator("pre code").Last).ToContainTextAsync("Fido");
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
