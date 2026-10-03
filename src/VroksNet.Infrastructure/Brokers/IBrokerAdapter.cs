using VroksNet.Application.Abstractions;
using VroksNet.Domain.Connections;

namespace VroksNet.Infrastructure.Brokers;

/// <summary>
/// Everything VroksNet does through one <see cref="ConnectionServiceType"/> (ADR 0003): reaching
/// it and sending through it. <see cref="Connections.ConnectionTester"/> and
/// <see cref="Connections.MessageSender"/> only pick the adapter for a connection's type from
/// <see cref="BrokerAdapterRegistry"/>. A type that can also be listened on implements
/// <see cref="IListeningBrokerAdapter"/>.
/// <para>
/// Every adapter opens its own short-lived client for each call — never an Aspire-wired one,
/// since a <see cref="Connection"/> holds whatever host and credentials the user typed — and
/// returns messages that are safe to show verbatim in the UI.
/// </para>
/// </summary>
public interface IBrokerAdapter
{
    ConnectionServiceType Type { get; }

    /// <summary>The broker options it reads (ADR 0003); empty if none. Create/update rejects any other name.</summary>
    IReadOnlyList<BrokerOptionDefinition> Options { get; }

    Task<ConnectionTestResult> TestAsync(Connection connection, CancellationToken cancellationToken);

    /// <summary>Succeeds only once the target has the message (see <see cref="IMessageSender"/>).</summary>
    Task<MessageSendResult> SendAsync(Connection connection, string operationKey, string? payload, BrokerOptions? options, CancellationToken cancellationToken);
}
