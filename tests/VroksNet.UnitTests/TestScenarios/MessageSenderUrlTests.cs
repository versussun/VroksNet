using VroksNet.Domain.Connections;
using VroksNet.Infrastructure.Connections;

namespace VroksNet.UnitTests.TestScenarios;

/// <summary>
/// Which URL <see cref="MessageSender"/> actually requests for an Http connection + operation.
/// Uses a capturing <see cref="HttpMessageHandler"/> instead of the network, so it only checks URL
/// composition — the real round-trip is covered in VroksNet.IntegrationTests.
/// </summary>
public class MessageSenderUrlTests
{
    [Theory]
    [InlineData("https://api.example.com", "https://api.example.com/pets")]
    [InlineData("https://api.example.com/", "https://api.example.com/pets")]
    [InlineData("https://api.example.com/v1", "https://api.example.com/v1/pets")]
    [InlineData("https://api.example.com/v1/", "https://api.example.com/v1/pets")]
    [InlineData("https://api.example.com/v1?api-key=abc", "https://api.example.com/v1/pets?api-key=abc")]
    public async Task SendAsync_Http_KeepsTheConnectionsBasePathAndQuery(string connectionUrl, string expectedUrl)
    {
        var handler = new CapturingHandler();
        var sender = new MessageSender(new SingleClientFactory(handler));

        await sender.SendAsync(
            new Connection { Id = Guid.NewGuid(), Name = "Pets API", ServiceType = ConnectionServiceType.Http, Value = connectionUrl },
            "GET /pets",
            null,
            null,
            TestContext.Current.CancellationToken);

        Assert.Equal(expectedUrl, handler.LastRequestUri?.AbsoluteUri);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("[]") });
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
