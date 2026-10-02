using System.Text;
using NATS.Client.Core;
using RabbitMQ.Client;
using VroksNet.Application.Abstractions;
using VroksNet.Domain.Connections;
using VroksNet.Domain.TestScenarios;

namespace VroksNet.Infrastructure.Connections;

/// <summary>
/// Actually sends a message to a <see cref="Connection"/>'s target: an HTTP request for
/// <see cref="ConnectionServiceType.Http"/> (built from the operation key's "METHOD /path"), or a
/// real broker publish for RabbitMq/Nats (routing key/subject = the operation key's channel
/// address — see <see cref="OperationCompatibility"/>). Deliberately doesn't reuse the
/// Aspire-wired dev "rabbitmq"/"nats" resources from AppHost.cs — same reasoning as
/// <see cref="ConnectionTester"/>, which this otherwise mirrors closely (see its doc comment and
/// .claude/CLAUDE.md "Infrastructure notes").
/// </summary>
public sealed class MessageSender(IHttpClientFactory httpClientFactory) : IMessageSender
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public Task<MessageSendResult> SendAsync(Connection connection, string operationKey, string? payload, CancellationToken cancellationToken) => connection.ServiceType switch
    {
        ConnectionServiceType.Http => SendHttpAsync(connection.Value, operationKey, payload, cancellationToken),
        ConnectionServiceType.RabbitMq => PublishRabbitMqAsync(connection.Value, operationKey, payload, cancellationToken),
        ConnectionServiceType.Nats => PublishNatsAsync(connection.Value, operationKey, payload, cancellationToken),
        _ => Task.FromResult(new MessageSendResult(false, $"Unsupported service type '{connection.ServiceType}'."))
    };

    private async Task<MessageSendResult> SendHttpAsync(string baseUrl, string operationKey, string? payload, CancellationToken cancellationToken)
    {
        if (!OperationCompatibility.IsHttpOperation(operationKey))
        {
            return new MessageSendResult(false, "This operation isn't HTTP-shaped (expected \"METHOD /path\") — it can't be sent to an Http connection.");
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
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

            var client = httpClientFactory.CreateClient(nameof(MessageSender));
            using var response = await client.SendAsync(request, cancellationToken).WaitAsync(Timeout, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return new MessageSendResult(true, $"{(int)response.StatusCode} {response.StatusCode}", body, (int)response.StatusCode);
        }
        catch (TimeoutException)
        {
            return new MessageSendResult(false, $"Timed out after {Timeout.TotalSeconds:0}s.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new MessageSendResult(false, ex.Message);
        }
    }

    private static async Task<MessageSendResult> PublishRabbitMqAsync(string connectionString, string operationKey, string? payload, CancellationToken cancellationToken)
    {
        var channelAddress = ChannelAddressOf(operationKey);
        if (channelAddress is null)
        {
            return new MessageSendResult(false, "This operation isn't AsyncAPI-shaped (expected \"channel:action\") — it can't be published to a RabbitMq connection.");
        }

        if (!Uri.TryCreate(connectionString, UriKind.Absolute, out var uri))
        {
            return new MessageSendResult(false, "Not a valid amqp(s):// connection string.");
        }

        try
        {
            var factory = new ConnectionFactory { Uri = uri };
            await using var connection = await factory.CreateConnectionAsync(cancellationToken).WaitAsync(Timeout, cancellationToken);
            await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

            // The default exchange ("") routes by routing key = queue name — simplest reasonable
            // choice here since a TestScenario has no separate "exchange" concept to configure.
            await channel.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: channelAddress,
                body: Encoding.UTF8.GetBytes(payload ?? string.Empty),
                cancellationToken: cancellationToken);

            return new MessageSendResult(true, $"Published to routing key \"{channelAddress}\".");
        }
        catch (TimeoutException)
        {
            return new MessageSendResult(false, $"Timed out after {Timeout.TotalSeconds:0}s.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new MessageSendResult(false, ex.Message);
        }
    }

    private static async Task<MessageSendResult> PublishNatsAsync(string connectionString, string operationKey, string? payload, CancellationToken cancellationToken)
    {
        var subject = ChannelAddressOf(operationKey);
        if (subject is null)
        {
            return new MessageSendResult(false, "This operation isn't AsyncAPI-shaped (expected \"channel:action\") — it can't be published to a Nats connection.");
        }

        try
        {
            await using var connection = new NatsConnection(new NatsOpts { Url = connectionString });
            await connection.PublishAsync(subject, payload ?? string.Empty, cancellationToken: cancellationToken).AsTask().WaitAsync(Timeout, cancellationToken);
            return new MessageSendResult(true, $"Published to subject \"{subject}\".");
        }
        catch (TimeoutException)
        {
            return new MessageSendResult(false, $"Timed out after {Timeout.TotalSeconds:0}s.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new MessageSendResult(false, ex.Message);
        }
    }

    /// <summary>AsyncAPI operation keys are "channel/address:action" — the channel address is everything before the last ':'; null if the key isn't AsyncAPI-shaped at all.</summary>
    private static string? ChannelAddressOf(string operationKey)
    {
        if (OperationCompatibility.IsHttpOperation(operationKey))
        {
            return null;
        }

        var separatorIndex = operationKey.LastIndexOf(':');
        return separatorIndex > 0 ? operationKey[..separatorIndex] : null;
    }
}
