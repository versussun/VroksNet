namespace VroksNet.IntegrationTests.Brokers.Redis;

/// <summary>Every Redis test class shares one <see cref="RedisAppHostFixture"/> and runs sequentially against it.</summary>
[CollectionDefinition("AppHost.Redis")]
public sealed class RedisCollection : ICollectionFixture<RedisAppHostFixture>;
