using Amazon.SQS.Model;
using VroksNet.Application.Abstractions;
using VroksNet.Domain.Connections;
using VroksNet.Domain.TestScenarios;

namespace VroksNet.Infrastructure.Brokers.Aws;

/// <summary>
/// <see cref="ConnectionServiceType.Sqs"/> (N5 of docs/broker-adapters-plan.md), through AWSSDK.SQS.
/// The connection value is an <see cref="AwsConnectionValue"/>; the channel address is the queue's
/// name or URL. Send only (ADR 0003): a queue's consumers compete for its messages, so listening
/// would take them away — the traits say so, and Listen goes through an SNS topic instead.
/// </summary>
public sealed class SqsBrokerAdapter : IBrokerAdapter
{
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(10);

    public ConnectionServiceType Type => ConnectionServiceType.Sqs;

    public IReadOnlyList<BrokerOptionDefinition> Options { get; } = [AwsClients.MessageGroupIdDefinition("queue")];

    public Task<ConnectionTestResult> TestAsync(Connection connection, CancellationToken cancellationToken)
        => AwsClients.TestAsync(connection.Value, cancellationToken);

    public async Task<MessageSendResult> SendAsync(Connection connection, string operationKey, string? payload, BrokerOptions? options, CancellationToken cancellationToken)
    {
        var address = OperationCompatibility.ChannelAddressOf(operationKey);
        if (address is null)
        {
            return new MessageSendResult(false, "This operation isn't AsyncAPI-shaped (expected \"channel:action\") — it can't be sent to an SQS connection.");
        }

        if (AwsConnectionValue.Parse(connection.Value) is not { } value)
        {
            return new MessageSendResult(false, AwsConnectionValue.InvalidMessage);
        }

        if (string.IsNullOrEmpty(payload))
        {
            return new MessageSendResult(false, "SQS can't send an empty message — the operation has no example to send.");
        }

        var queue = AwsClients.NameOf(address);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(SendTimeout);
        try
        {
            using var sqs = AwsClients.Sqs(value, SendTimeout);
            var request = new SendMessageRequest { QueueUrl = await AwsClients.QueueUrlAsync(sqs, address, timeoutCts.Token), MessageBody = payload };
            if (AwsClients.IsFifo(address))
            {
                request.MessageGroupId = options?[AwsClients.MessageGroupIdOption] ?? AwsClients.DefaultMessageGroupId;
                request.MessageDeduplicationId = Guid.NewGuid().ToString("N");
            }

            var sent = await sqs.SendMessageAsync(request, timeoutCts.Token);
            return new MessageSendResult(true, $"Sent message {sent.MessageId} to queue \"{queue}\".");
        }
        catch (Exception ex) when (AwsClients.IsTimeout(ex, cancellationToken))
        {
            return new MessageSendResult(false, $"Timed out after {SendTimeout.TotalSeconds:0}s.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new MessageSendResult(false, AwsClients.MessageOf(ex, $"queue \"{queue}\""));
        }
    }
}
