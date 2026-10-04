namespace VroksNet.IntegrationTests.Brokers.Aws;

/// <summary>Every AWS test class shares one <see cref="AwsAppHostFixture"/> and runs sequentially against it.</summary>
[CollectionDefinition("AppHost.Aws")]
public sealed class AwsCollection : ICollectionFixture<AwsAppHostFixture>;
