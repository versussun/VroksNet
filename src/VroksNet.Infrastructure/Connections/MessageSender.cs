using System.Text;
using Confluent.Kafka;
using NATS.Client.Core;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using VroksNet.Application.Abstractions;
using VroksNet.Domain.Connections;
using VroksNet.Domain.TestScenarios;

namespace VroksNet.Infrastructure.Connections;

/// <summary>
/// Actually sends a message to a <see cref="Connection"/>'s target: an HTTP request for
/// <see cref="ConnectionServiceType.Http"/> (built from the operation key's "METHOD /path"), or a
/// real broker publish for RabbitMq/Nats/Kafka (routing key/subject/topic = the operation key's
/// channel address — see <see cref="OperationCompatibility"/>). Deliberately doesn't reuse the
/// Aspire-wired dev "rabbitmq"/"nats"/"kafka" resources from AppHost.cs — same reasoning as
/// <see cref="ConnectionTester"/>, which this otherwise mirrors closely (see its doc comment and
/// .claude/CLAUDE.md "Infrastructure notes").
/// </summary>
public sealed class MessageSender(IHttpClientFactory httpClientFactory) : IMessageSender
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public Task<MessageSendResult> SendAsync(Connection connection, string operationKey, string? payload, string? exchange, CancellationToken cancellationToken) => connection.ServiceType switch
    {
        ConnectionServiceType.Http => SendHttpAsync(connection.Value, operationKey, payload, cancellationToken),
        ConnectionServiceType.RabbitMq => PublishRabbitMqAsync(connection.Value, operationKey, payload, exchange ?? string.Empty, cancellationToken),
        ConnectionServiceType.Nats => PublishNatsAsync(connection.Value, operationKey, payload, cancellationToken),
        ConnectionServiceType.Kafka => PublishKafkaAsync(connection.Value, operationKey, payload, cancellationToken),
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

    private static async Task<MessageSendResult> PublishRabbitMqAsync(string connectionString, string operationKey, string? payload, string exchange, CancellationToken cancellationToken)
    {
        var channelAddress = OperationCompatibility.ChannelAddressOf(operationKey);
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
            await using var connection = await RabbitMqConnections.OpenAsync(uri, Timeout, cancellationToken);
            await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

            // Publishing to a missing exchange isn't an error the publish reports — the broker closes
            // the channel afterwards — so check it exists first. The default exchange ("") always
            // does; it routes by routing key = queue name.
            if (exchange.Length > 0)
            {
                await channel.ExchangeDeclarePassiveAsync(exchange, cancellationToken);
            }

            await channel.BasicPublishAsync(
                exchange: exchange,
                routingKey: channelAddress,
                body: Encoding.UTF8.GetBytes(payload ?? string.Empty),
                cancellationToken: cancellationToken);

            return new MessageSendResult(true, exchange.Length > 0
                ? $"Published to exchange \"{exchange}\", routing key \"{channelAddress}\"."
                : $"Published to routing key \"{channelAddress}\".");
        }
        catch (OperationInterruptedException ex) when (ex.ShutdownReason?.ReplyCode == 404)
        {
            return new MessageSendResult(false, $"Exchange \"{exchange}\" doesn't exist on this broker.");
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
        var subject = OperationCompatibility.ChannelAddressOf(operationKey);
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

    private static async Task<MessageSendResult> PublishKafkaAsync(string connectionString, string operationKey, string? payload, CancellationToken cancellationToken)
    {
        var topic = OperationCompatibility.ChannelAddressOf(operationKey);
        if (topic is null)
        {
            return new MessageSendResult(false, "This operation isn't AsyncAPI-shaped (expected \"channel:action\") — it can't be published to a Kafka connection.");
        }

        var config = KafkaClients.ConfigFrom(connectionString);
        if (config is null)
        {
            return new MessageSendResult(false, KafkaClients.InvalidConnectionStringMessage);
        }

        try
        {
            using var producer = KafkaClients.CreateProducer(config, Timeout);
            // Completes once the broker acknowledges the write (or message.timeout.ms runs out),
            // so success here means the message really is on the topic.
            var delivery = await producer.ProduceAsync(topic, new Message<Null, string> { Value = payload ?? string.Empty }, cancellationToken);
            return new MessageSendResult(true, $"Published to topic \"{topic}\" (partition {delivery.Partition.Value}, offset {delivery.Offset.Value}).");
        }
        catch (ProduceException<Null, string> ex) when (ex.Error.Code == ErrorCode.Local_MsgTimedOut)
        {
            return new MessageSendResult(false, $"Timed out after {Timeout.TotalSeconds:0}s — the broker didn't acknowledge the message.");
        }
        catch (ProduceException<Null, string> ex) when (ex.Error.Code is ErrorCode.UnknownTopicOrPart or ErrorCode.Local_UnknownTopic)
        {
            return new MessageSendResult(false, $"Topic \"{topic}\" doesn't exist on this broker (and the broker doesn't auto-create topics).");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new MessageSendResult(false, ex is KafkaException kafka ? kafka.Error.Reason : ex.Message);
        }
    }
}
