using System.Text.RegularExpressions;
using VroksNet.E2ETests.Fixtures;
using static Microsoft.Playwright.Assertions;

namespace VroksNet.E2ETests;

[Collection("AppHost")]
public sealed class HomeNavigationTests(AppHostFixture fixture) : PageTestBase(fixture)
{
    [Fact]
    public async Task HomePage_ShowsWelcomeHeading()
    {
        await Page.GotoAsync("/");

        await Expect(Page.Locator("h1")).ToHaveTextAsync("VroksNet");
    }

    [Theory]
    [InlineData("Specifications", "/specifications")]
    [InlineData("Test Scenarios", "/test-scenarios")]
    [InlineData("Settings", "/settings")]
    public async Task NavMenu_NavigatesToPage(string linkText, string expectedPath)
    {
        await Page.GotoAsync("/");

        // Scoped to the <nav> landmark — the home page's own body now also links to
        // Specifications/Test Scenarios/Settings (see Home.razor), so an unscoped GetByRole
        // would match both and fail Playwright's strict-mode uniqueness check.
        await Page.GetByRole(AriaRole.Navigation).GetByRole(AriaRole.Link, new() { Name = linkText, Exact = true }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex(Regex.Escape(expectedPath) + "$"));
    }
}
