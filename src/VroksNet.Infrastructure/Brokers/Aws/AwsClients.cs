using Amazon.Runtime;
using Amazon.SecurityToken;
using Amazon.SecurityToken.Model;
using Amazon.SimpleNotificationService;
using Amazon.SQS;
using Amazon.SQS.Model;
using VroksNet.Application.Abstractions;

namespace VroksNet.Infrastructure.Brokers.Aws;

/// <summary>
/// What the SQS and SNS adapters share: short-lived clients built from an
/// <see cref="AwsConnectionValue"/>, the connection check, resolving a queue or topic from a
/// channel address, and messages safe to show.
/// </summary>
public static class AwsClients
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    /// <summary>The message group of a FIFO send when the <c>messageGroupId</c> option is blank.</summary>
    public const string DefaultMessageGroupId = "vroksnet";

    public const string MessageGroupIdOption = "messageGroupId";

    public static BrokerOptionDefinition MessageGroupIdDefinition(string entity) => new(
        MessageGroupIdOption,
        "Message group ID",
        SendDescription: $"For a FIFO {entity} (its name ends in \".fifo\"): the message group to send in. Ignored otherwise. Blank means \"{DefaultMessageGroupId}\".",
        SendPlaceholder: $"{DefaultMessageGroupId} (default)");

    public static AmazonSQSClient Sqs(AwsConnectionValue value, TimeSpan timeout)
        => value.Credentials() is { } credentials
            ? new AmazonSQSClient(credentials, value.Configure(new AmazonSQSConfig(), timeout))
            : new AmazonSQSClient(value.Configure(new AmazonSQSConfig(), timeout));

    public static AmazonSimpleNotificationServiceClient Sns(AwsConnectionValue value, TimeSpan timeout)
        => value.Credentials() is { } credentials
            ? new AmazonSimpleNotificationServiceClient(credentials, value.Configure(new AmazonSimpleNotificationServiceConfig(), timeout))
            : new AmazonSimpleNotificationServiceClient(value.Configure(new AmazonSimpleNotificationServiceConfig(), timeout));

    public static AmazonSecurityTokenServiceClient Sts(AwsConnectionValue value, TimeSpan timeout)
        => value.Credentials() is { } credentials
            ? new AmazonSecurityTokenServiceClient(credentials, value.Configure(new AmazonSecurityTokenServiceConfig(), timeout))
            : new AmazonSecurityTokenServiceClient(value.Configure(new AmazonSecurityTokenServiceConfig(), timeout));

    /// <summary>
    /// The connection check for both types: STS's "who am I" works with any valid credentials and
    /// needs no permission, so a send-only policy still passes.
    /// </summary>
    public static async Task<ConnectionTestResult> TestAsync(string connectionValue, CancellationToken cancellationToken)
    {
        if (AwsConnectionValue.Parse(connectionValue) is not { } value)
        {
            return new ConnectionTestResult(false, AwsConnectionValue.InvalidMessage);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TestTimeout);
        try
        {
            using var sts = Sts(value, TestTimeout);
            var identity = await sts.GetCallerIdentityAsync(new GetCallerIdentityRequest(), timeoutCts.Token);
            return new ConnectionTestResult(true, $"Connected — account {identity.Account}.");
        }
        catch (Exception ex) when (IsTimeout(ex, cancellationToken))
        {
            return new ConnectionTestResult(false, $"Timed out after {TestTimeout.TotalSeconds:0}s.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ConnectionTestResult(false, MessageOf(ex, "account"));
        }
    }

    /// <summary>The queue's URL: the address itself if it's one, otherwise the URL of the queue with that name.</summary>
    public static async Task<string> QueueUrlAsync(IAmazonSQS sqs, string address, CancellationToken cancellationToken)
        => IsUrl(address)
            ? address
            : (await sqs.GetQueueUrlAsync(new GetQueueUrlRequest { QueueName = address }, cancellationToken)).QueueUrl;

    /// <summary>
    /// The topic's ARN: the address itself if it's one, otherwise the ARN of the topic with that
    /// name in the credentials' own account and the value's region (a topic in another account
    /// needs its ARN).
    /// </summary>
    public static async Task<string> TopicArnAsync(AwsConnectionValue value, string address, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (address.StartsWith("arn:", StringComparison.Ordinal))
        {
            return address;
        }

        using var sts = Sts(value, timeout);
        var identity = await sts.GetCallerIdentityAsync(new GetCallerIdentityRequest(), cancellationToken);
        // The caller's own ARN (arn:<partition>:iam::…) says which partition the account is in.
        var partition = identity.Arn.Split(':') is [_, var name, ..] ? name : "aws";
        return $"arn:{partition}:sns:{value.Region}:{identity.Account}:{address}";
    }

    /// <summary>Whether the queue or topic an address names is a FIFO one (its name ends in ".fifo").</summary>
    public static bool IsFifo(string address) => address.EndsWith(".fifo", StringComparison.Ordinal);

    /// <summary>The name in a queue URL or topic ARN (its last part); the address itself otherwise.</summary>
    public static string NameOf(string address)
        => IsUrl(address) ? address.TrimEnd('/')[(address.TrimEnd('/').LastIndexOf('/') + 1)..]
            : address.StartsWith("arn:", StringComparison.Ordinal) ? address[(address.LastIndexOf(':') + 1)..]
            : address;

    /// <summary>The adapter's own budget ran out (not the caller's cancellation), or the client gave up waiting.</summary>
    public static bool IsTimeout(Exception exception, CancellationToken cancellationToken)
        => (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
            || exception is TimeoutException
            || exception.InnerException is TimeoutException or TaskCanceledException && !cancellationToken.IsCancellationRequested;

    /// <summary>Whether AWS turned the request down for lack of a permission (the credentials themselves were fine).</summary>
    public static bool IsAccessDenied(Exception exception)
        => exception is AmazonServiceException { ErrorCode: "AccessDenied" or "AccessDeniedException" or "AuthorizationError" }
            or Amazon.SimpleNotificationService.Model.AuthorizationErrorException;

    /// <summary>A message safe to show for <paramref name="exception"/>; <paramref name="what"/> names the queue or topic.</summary>
    public static string MessageOf(Exception exception, string what) => exception switch
    {
        AmazonServiceException { ErrorCode: "InvalidClientTokenId" or "SignatureDoesNotMatch" or "UnrecognizedClientException" or "InvalidAccessKeyId" or "ExpiredToken" or "ExpiredTokenException" }
            => "AWS refused the credentials.",
        _ when IsAccessDenied(exception) => $"The credentials lack a permission for this: {exception.Message}",
        QueueDoesNotExistException or Amazon.SimpleNotificationService.Model.NotFoundException
            or AmazonServiceException { ErrorCode: "AWS.SimpleQueueService.NonExistentQueue" or "NotFound" } => $"No {what}.",
        AmazonClientException when exception.Message.Contains("credentials", StringComparison.OrdinalIgnoreCase)
            => "No AWS credentials: put AccessKeyId and SecretAccessKey in the connection value, or give VroksNet's environment AWS credentials.",
        AmazonClientException { InnerException: HttpRequestException } or HttpRequestException => "Couldn't reach AWS (or the connection's ServiceUrl).",
        _ => exception.Message
    };

    private static bool IsUrl(string address)
        => address.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || address.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
}
