using VroksNet.Infrastructure.Hosting;

namespace VroksNet.UnitTests.Hosting;

/// <summary>How ApiService's existing addresses, the provider port and the gRPC port are merged — and the collisions it refuses.</summary>
public sealed class ListenAddressesTests
{
    [Fact]
    public void Merge_KeepsAspnetcoreUrls_AndStaysOnLoopbackWhenTheyAre()
        => Assert.Equal(
            ["https://localhost:7001", "http://127.0.0.1:5001", "http://localhost:7353"],
            Urls("https://localhost:7001;http://127.0.0.1:5001", null, null, 7353));

    [Fact]
    public void Merge_FallsBackToHttpPorts_AndBindsAllInterfacesLikeThem()
        => Assert.Equal(["http://*:8080", "http://*:7353"], Urls(null, "8080", null, 7353));

    [Fact]
    public void Merge_IncludesHttpsPorts()
        => Assert.Equal(["http://*:8080", "https://*:8443", "http://*:7353"], Urls("", "8080", "8443", 7353));

    [Fact]
    public void Merge_NothingConfigured_KeepsKestrelsDefault()
        => Assert.Equal(["http://localhost:5000", "http://localhost:7353"], Urls(null, null, null, 7353));

    [Fact]
    public void Merge_MixedHosts_BindsAllInterfaces()
        => Assert.Equal(["http://localhost:5000", "http://+:6000", "http://*:7353"], Urls("http://localhost:5000;http://+:6000", null, null, 7353));

    [Theory]
    [InlineData("http://*:7353", null)]
    [InlineData(null, "7353")]
    [InlineData("http://localhost:7353", null)]
    public void Merge_PortAlreadyUsedByTheApi_Throws(string? urls, string? httpPorts)
        => Assert.Throws<InvalidOperationException>(() => Urls(urls, httpPorts, null, 7353));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(70000)]
    public void Merge_NotAFixedPort_Throws(int port)
        => Assert.Throws<InvalidOperationException>(() => Urls(null, "8080", null, port));

    [Fact]
    public void Merge_TheGrpcPortIsHttp2Only_AndFollowsTheLoopbackRule()
    {
        Assert.Equal(
            [new ListenAddress("https://localhost:7352"), new ListenAddress("http://localhost:7353"), new ListenAddress("http://localhost:7354", Http2Only: true)],
            ListenAddresses.Merge("https://localhost:7352", null, null, 7353, 7354));
        Assert.Equal(
            [new ListenAddress("http://*:8080"), new ListenAddress("http://*:7354", Http2Only: true)],
            ListenAddresses.Merge(null, "8080", null, null, 7354));
    }

    [Theory]
    [InlineData(7353, 7353)]
    [InlineData(null, 8080)]
    public void Merge_TheGrpcPortCollidingWithAnother_Throws(int? providerPort, int grpcPort)
        => Assert.Throws<InvalidOperationException>(() => ListenAddresses.Merge(null, "8080", null, providerPort, grpcPort));

    private static IEnumerable<string> Urls(string? urls, string? httpPorts, string? httpsPorts, int providerPort)
        => ListenAddresses.Merge(urls, httpPorts, httpsPorts, providerPort, grpcPort: null).Select(address => address.Url);
}
