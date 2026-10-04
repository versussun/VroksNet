using Amazon.Runtime;
using Amazon.SimpleNotificationService;
using Amazon.SQS;
using VroksNet.IntegrationTests.Fixtures;

namespace VroksNet.IntegrationTests.Brokers.Aws;

/// <summary>
/// The graph with only LocalStack (AppHost.cs "localstack", emulating SQS, SNS and STS) — the N5
/// broker family. LocalStack takes any credentials and creates whatever it's asked to, so tests
/// make their own GUID-named queues and topics.
/// </summary>
public sealed class AwsAppHostFixture() : AppHostFixtureBase(["localstack"])
{
    public const string Region = "us-east-1";

    /// <summary>LocalStack's gateway as an Sqs or Sns connection value (Aspire hands out no connection string for AWS).</summary>
    public string ConnectionValue => $"Region={Region};AccessKeyId=test;SecretAccessKey=test;ServiceUrl={ServiceUrl}";

    public Uri ServiceUrl => App.GetEndpoint("localstack", "gateway");

    public AmazonSQSClient Sqs() => new(new BasicAWSCredentials("test", "test"), new AmazonSQSConfig { ServiceURL = ServiceUrl.ToString(), AuthenticationRegion = Region });

    public AmazonSimpleNotificationServiceClient Sns() => new(new BasicAWSCredentials("test", "test"), new AmazonSimpleNotificationServiceConfig { ServiceURL = ServiceUrl.ToString(), AuthenticationRegion = Region });
}
