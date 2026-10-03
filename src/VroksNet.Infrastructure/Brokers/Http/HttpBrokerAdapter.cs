using System.Text;
using VroksNet.Application.Abstractions;
using VroksNet.Domain.Connections;
using VroksNet.Domain.TestScenarios;

namespace VroksNet.Infrastructure.Brokers.Http;

/// <summary>
/// <see cref="ConnectionServiceType.Http"/>: test and Send, no Listen. The check is a short-timeout
/// GET where any HTTP response counts as reachable — even a 404/401 proves DNS+TCP+TLS all worked,
/// which is the point; only a thrown exception counts as failure. Send is the HTTP request the
/// operation key ("METHOD /path") describes.
/// </summary>
public sealed class HttpBrokerAdapter(IHttpClientFactory httpClientFactory) : IBrokerAdapter
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(10);

    public ConnectionServiceType Type => ConnectionServiceType.Http;

    public async Task<ConnectionTestResult> TestAsync(Connection connection, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(connection.Value, UriKind.Absolute, out var uri))
        {
            return new ConnectionTestResult(false, "Not a valid absolute URL.");
        }

        try
        {
            var client = httpClientFactory.CreateClient(nameof(HttpBrokerAdapter));
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .WaitAsync(TestTimeout, cancellationToken);
            return new ConnectionTestResult(true, $"Reached {uri.Host} — responded {(int)response.StatusCode} {response.StatusCode}.");
        }
        catch (TimeoutException)
        {
            return new ConnectionTestResult(false, $"Timed out after {TestTimeout.TotalSeconds:0}s.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ConnectionTestResult(false, ex.Message);
        }
    }

    public async Task<MessageSendResult> SendAsync(Connection connection, string operationKey, string? payload, string? exchange, CancellationToken cancellationToken)
    {
        if (!OperationCompatibility.IsHttpOperation(operationKey))
        {
            return new MessageSendResult(false, "This operation isn't HTTP-shaped (expected \"METHOD /path\") — it can't be sent to an Http connection.");
        }

        if (!Uri.TryCreate(connection.Value, UriKind.Absolute, out var baseUri))
        {
            return new MessageSendResult(false, "Connection's value isn't a valid absolute URL.");
        }

        var parts = operationKey.Split(' ', 2);
        // Appends the operation's path to the connection's own base path ("https://host/v1" +
        // "/pets" → "https://host/v1/pets"), keeping the connection's query string (e.g. an
        // "?api-key=…"). new Uri(baseUri, "/pets") would resolve the absolute path against the
        // host and silently drop both.
        var uri = new Uri(baseUri.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/" + parts[1].TrimStart('/') + baseUri.Query);

        try
        {
            using var request = new HttpRequestMessage(new HttpMethod(parts[0]), uri);
            if (payload is not null)
            {
                request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            }

            var client = httpClientFactory.CreateClient(nameof(HttpBrokerAdapter));
            using var response = await client.SendAsync(request, cancellationToken).WaitAsync(SendTimeout, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return new MessageSendResult(true, $"{(int)response.StatusCode} {response.StatusCode}", body, (int)response.StatusCode);
        }
        catch (TimeoutException)
        {
            return new MessageSendResult(false, $"Timed out after {SendTimeout.TotalSeconds:0}s.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new MessageSendResult(false, ex.Message);
        }
    }
}
