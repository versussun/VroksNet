using VroksNet.IntegrationTests.Fixtures;

namespace VroksNet.IntegrationTests.Brokers.ServiceBus;

/// <summary>
/// The graph with only the Service Bus emulator (AppHost.cs "servicebus") — the N4 broker family.
/// The emulator only has the entities AppHost declares: <see cref="Queue"/>, and <see cref="Topic"/>
/// with subscription <see cref="Subscription"/>.
/// </summary>
public sealed class ServiceBusAppHostFixture() : AppHostFixtureBase(["servicebus"])
{
    public const string Queue = "warehouse.picking.requested";
    public const string Topic = "warehouse.stock.changed";
    public const string Subscription = "vroksnet";

    /// <summary>
    /// The emulator as a ServiceBus connection value: exactly the connection string Aspire's
    /// <c>WithReference</c> hands out (<c>Endpoint=sb://…;…;UseDevelopmentEmulator=true</c>), which
    /// is what the Aspire package passes through <c>valueFrom</c>.
    /// </summary>
    public async Task<string> ConnectionValueAsync(CancellationToken cancellationToken)
        => await App.GetConnectionStringAsync("servicebus", cancellationToken)
            ?? throw new InvalidOperationException("The servicebus resource has no connection string.");
}
