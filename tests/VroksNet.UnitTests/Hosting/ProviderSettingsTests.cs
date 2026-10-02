using Microsoft.Extensions.Configuration;
using VroksNet.Infrastructure.Hosting;

namespace VroksNet.UnitTests.Hosting;

public class ProviderSettingsTests
{
    [Theory]
    [InlineData(null, new string[0])]
    [InlineData("", new string[0])]
    [InlineData(" , ", new string[0])]
    [InlineData("http://localhost:5173", new[] { "http://localhost:5173" })]
    [InlineData(" http://a.example/ , https://b.example,http://A.example ", new[] { "http://a.example", "https://b.example" })]
    [InlineData("http://a.example, *", new[] { "*" })]
    public void CorsOriginsFrom_ParsesTheCommaSeparatedList(string? value, string[] expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [ProviderSettings.CorsOriginsConfigKey] = value })
            .Build();

        Assert.Equal(expected, ProviderSettings.CorsOriginsFrom(configuration));
    }
}
