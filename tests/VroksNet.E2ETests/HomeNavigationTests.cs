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

        await Expect(Page.Locator("h1")).ToHaveTextAsync("Hello, world!");
    }

    [Theory]
    [InlineData("Specifications", "/specifications")]
    [InlineData("Weather", "/weather")]
    [InlineData("Settings", "/settings")]
    public async Task NavMenu_NavigatesToPage(string linkText, string expectedPath)
    {
        await Page.GotoAsync("/");

        await Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = linkText }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex(Regex.Escape(expectedPath) + "$"));
    }
}
