using VroksNet.Domain.Connections;

namespace VroksNet.Application.Abstractions;

/// <summary>
/// What each connection type's broker adapter accepts (ADR 0003), so create/update can reject what
/// would only fail at run time: the broker options it declares, and whether it can subscribe to a
/// channel.
/// </summary>
public interface IBrokerRules
{
    /// <summary>The options <paramref name="type"/> accepts; empty if none (or the type is unknown).</summary>
    IReadOnlyList<BrokerOptionDefinition> OptionsOf(ConnectionServiceType type);

    /// <summary>
    /// Why a Listen on <paramref name="channelAddress"/> can't work through a <paramref name="type"/>
    /// connection with the scenario's (already normalized) <paramref name="options"/> — e.g. its
    /// wildcards can't match the channel's parameters; null if it can.
    /// </summary>
    string? WhyCantListen(ConnectionServiceType type, string channelAddress, BrokerOptions? options);
}
