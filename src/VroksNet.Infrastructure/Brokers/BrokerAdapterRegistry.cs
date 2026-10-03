using VroksNet.Application.Abstractions;
using VroksNet.Domain.Connections;
using VroksNet.Domain.TestScenarios;

namespace VroksNet.Infrastructure.Brokers;

/// <summary>
/// The registered <see cref="IBrokerAdapter"/>s by <see cref="ConnectionServiceType"/>. Building it
/// throws if two adapters claim the same type. It's also what Application asks, as
/// <see cref="IBrokerRules"/>, about each type's options and channels.
/// </summary>
public sealed class BrokerAdapterRegistry(IEnumerable<IBrokerAdapter> adapters) : IBrokerRules
{
    private readonly Dictionary<ConnectionServiceType, IBrokerAdapter> adaptersByType = adapters.ToDictionary(adapter => adapter.Type);

    /// <summary>The adapter for <paramref name="type"/>; null if none is registered.</summary>
    public IBrokerAdapter? Find(ConnectionServiceType type) => adaptersByType.GetValueOrDefault(type);

    public IReadOnlyList<BrokerOptionDefinition> OptionsOf(ConnectionServiceType type) => Find(type)?.Options ?? [];

    public string? WhyCantListen(ConnectionServiceType type, string channelAddress, BrokerOptions? options)
        => Find(type) is IListeningBrokerAdapter adapter && ChannelPattern.Parse(channelAddress) is { } channel
            ? adapter.WhyCantListen(channel, options)
            : null;
}
