using VroksNet.Domain.Connections;

namespace VroksNet.Infrastructure.Brokers;

/// <summary>
/// The registered <see cref="IBrokerAdapter"/>s by <see cref="ConnectionServiceType"/>. Building it
/// throws if two adapters claim the same type.
/// </summary>
public sealed class BrokerAdapterRegistry(IEnumerable<IBrokerAdapter> adapters)
{
    private readonly Dictionary<ConnectionServiceType, IBrokerAdapter> adaptersByType = adapters.ToDictionary(adapter => adapter.Type);

    /// <summary>The adapter for <paramref name="type"/>; null if none is registered.</summary>
    public IBrokerAdapter? Find(ConnectionServiceType type) => adaptersByType.GetValueOrDefault(type);
}
