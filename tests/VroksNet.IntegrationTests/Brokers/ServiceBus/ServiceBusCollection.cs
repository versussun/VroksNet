namespace VroksNet.IntegrationTests.Brokers.ServiceBus;

/// <summary>Every Service Bus test class shares one <see cref="ServiceBusAppHostFixture"/> and runs sequentially against it.</summary>
[CollectionDefinition("AppHost.ServiceBus")]
public sealed class ServiceBusCollection : ICollectionFixture<ServiceBusAppHostFixture>;
