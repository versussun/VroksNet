using VroksNet.Infrastructure.Hosting;

namespace VroksNet.UnitTests.Hosting;

/// <summary>How ApiService's existing addresses and the provider port are merged — and the collisions it refuses.</summary>
public sealed class ProviderListenAddressesTests
{
    [Fact]
    public void Merge_KeepsAspnetcoreUrls_AndStaysOnLoopbackWhenTheyAre()
        => Assert.Equal(
            ["https://localhost:7001", "http://127.0.0.1:5001", "http://localhost:7353"],
            ProviderListenAddresses.Merge("https://localhost:7001;http://127.0.0.1:5001", null, null, 7353));

    [Fact]
    public void Merge_FallsBackToHttpPorts_AndBindsAllInterfacesLikeThem()
        => Assert.Equal(["http://*:8080", "http://*:7353"], ProviderListenAddresses.Merge(null, "8080", null, 7353));

    [Fact]
    public void Merge_IncludesHttpsPorts()
        => Assert.Equal(["http://*:8080", "https://*:8443", "http://*:7353"], ProviderListenAddresses.Merge("", "8080", "8443", 7353));

    [Fact]
    public void Merge_NothingConfigured_KeepsKestrelsDefault()
        => Assert.Equal(["http://localhost:5000", "http://localhost:7353"], ProviderListenAddresses.Merge(null, null, null, 7353));

    [Fact]
    public void Merge_MixedHosts_BindsAllInterfaces()
        => Assert.Equal(["http://localhost:5000", "http://+:6000", "http://*:7353"], ProviderListenAddresses.Merge("http://localhost:5000;http://+:6000", null, null, 7353));

    [Theory]
    [InlineData("http://*:7353", null)]
    [InlineData(null, "7353")]
    [InlineData("http://localhost:7353", null)]
    public void Merge_PortAlreadyUsedByTheApi_Throws(string? urls, string? httpPorts)
        => Assert.Throws<InvalidOperationException>(() => ProviderListenAddresses.Merge(urls, httpPorts, null, 7353));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(70000)]
    public void Merge_NotAFixedPort_Throws(int port)
        => Assert.Throws<InvalidOperationException>(() => ProviderListenAddresses.Merge(null, "8080", null, port));
}
