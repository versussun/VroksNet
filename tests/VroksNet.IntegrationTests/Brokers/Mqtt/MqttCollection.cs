namespace VroksNet.IntegrationTests.Brokers.Mqtt;

/// <summary>Every MQTT test class shares one <see cref="MqttAppHostFixture"/> and runs sequentially against it.</summary>
[CollectionDefinition("AppHost.Mqtt")]
public sealed class MqttCollection : ICollectionFixture<MqttAppHostFixture>;
