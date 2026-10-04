using Amazon;
using Amazon.Runtime;

namespace VroksNet.Infrastructure.Brokers.Aws;

/// <summary>
/// What an <see cref="VroksNet.Domain.Connections.ConnectionServiceType.Sqs"/> or
/// <see cref="VroksNet.Domain.Connections.ConnectionServiceType.Sns"/> connection value says:
/// <c>Region=eu-west-1;AccessKeyId=…;SecretAccessKey=…[;SessionToken=…][;ServiceUrl=http://localhost:4566]</c>.
/// Keys are case-insensitive. Without the keys, the AWS SDK's default credentials are used (the
/// environment, a profile, an IAM role), which is how a container running in AWS is usually given
/// them. <see cref="ServiceUrl"/> points the clients at an emulator such as LocalStack. Aspire
/// hands out no connection string for AWS, so this format is VroksNet's own.
/// </summary>
public sealed record AwsConnectionValue(string Region, string? AccessKeyId, string? SecretAccessKey, string? SessionToken, Uri? ServiceUrl)
{
    private static readonly HashSet<string> Keys = new(["Region", "AccessKeyId", "SecretAccessKey", "SessionToken", "ServiceUrl"], StringComparer.OrdinalIgnoreCase);

    public const string InvalidMessage = "Not a valid AWS connection value (expected Region=eu-west-1;AccessKeyId=…;SecretAccessKey=… — or Region=… alone for the default AWS credentials; optionally SessionToken=… and ServiceUrl=http://… for an emulator).";

    /// <summary>The value's settings; null if a key is unknown or repeated, Region is missing, only one of the two keys is given, or ServiceUrl isn't an http(s) URL.</summary>
    public static AwsConnectionValue? Parse(string value)
    {
        var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // Split at the first "=": a session token may end with "=" padding.
            var separatorIndex = pair.IndexOf('=');
            if (separatorIndex <= 0)
            {
                return null;
            }

            var key = pair[..separatorIndex].Trim();
            var setting = pair[(separatorIndex + 1)..].Trim();
            if (!Keys.Contains(key) || setting.Length == 0 || !settings.TryAdd(key, setting))
            {
                return null;
            }
        }

        var accessKeyId = settings.GetValueOrDefault("AccessKeyId");
        var secretAccessKey = settings.GetValueOrDefault("SecretAccessKey");
        var sessionToken = settings.GetValueOrDefault("SessionToken");
        if (settings.GetValueOrDefault("Region") is not { } region
            || (accessKeyId is null) != (secretAccessKey is null)
            || (sessionToken is not null && accessKeyId is null))
        {
            return null;
        }

        Uri? serviceUrl = null;
        if (settings.GetValueOrDefault("ServiceUrl") is { } url
            && (!Uri.TryCreate(url, UriKind.Absolute, out serviceUrl) || serviceUrl.Scheme is not ("http" or "https")))
        {
            return null;
        }

        return new AwsConnectionValue(region, accessKeyId, secretAccessKey, sessionToken, serviceUrl);
    }

    /// <summary>The value's own credentials; null for the SDK's default ones.</summary>
    public AWSCredentials? Credentials()
        => AccessKeyId is null || SecretAccessKey is null
            ? null
            : SessionToken is null
                ? new BasicAWSCredentials(AccessKeyId, SecretAccessKey)
                : new SessionAWSCredentials(AccessKeyId, SecretAccessKey, SessionToken);

    /// <summary>Points <paramref name="config"/> at the region (or the service URL), with <paramref name="timeout"/> per request and no retrying.</summary>
    public TConfig Configure<TConfig>(TConfig config, TimeSpan timeout) where TConfig : ClientConfig
    {
        if (ServiceUrl is not null)
        {
            config.ServiceURL = ServiceUrl.ToString();
            config.AuthenticationRegion = Region;
        }
        else
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(Region);
        }

        config.Timeout = timeout;
        config.MaxErrorRetry = 0;
        return config;
    }
}
