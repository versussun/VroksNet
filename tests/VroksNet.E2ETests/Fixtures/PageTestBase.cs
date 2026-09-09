namespace VroksNet.E2ETests.Fixtures;

/// <summary>
/// Base for E2E test classes. xUnit constructs a fresh instance of the test class per test
/// method, so a fresh <see cref="IBrowserContext"/>/<see cref="IPage"/> per instance here means
/// fresh browser state (cookies, local storage) per test, while still reusing the one shared
/// browser process and app graph from <see cref="AppHostFixture"/>.
/// </summary>
public abstract class PageTestBase(AppHostFixture fixture) : IAsyncLifetime
{
    private IBrowserContext _context = null!;

    protected IPage Page { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        _context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = fixture.WebBaseAddress.ToString(),
        });
        Page = await _context.NewPageAsync();
    }

    public async ValueTask DisposeAsync() => await _context.CloseAsync();
}
