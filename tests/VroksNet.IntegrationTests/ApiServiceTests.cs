using VroksNet.IntegrationTests.Fixtures;

namespace VroksNet.IntegrationTests;

[Collection("AppHost")]
public sealed class ApiServiceTests(AppHostFixture fixture)
{
    [Fact]
    public async Task GetRoot_ReturnsOk()
    {
        var response = await fixture.ApiServiceClient.GetAsync("/", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetHealth_ReturnsHealthy()
    {
        var response = await fixture.ApiServiceClient.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
