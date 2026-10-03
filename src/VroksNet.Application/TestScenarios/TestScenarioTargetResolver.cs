using VroksNet.Application.Abstractions;
using VroksNet.Application.Connections;
using VroksNet.Domain.Connections;
using VroksNet.Domain.TestScenarios;

namespace VroksNet.Application.TestScenarios;

/// <summary>Shared by <c>CreateTestScenarioHandler</c>/<c>UpdateTestScenarioHandler</c> so both validate the same way.</summary>
public static class TestScenarioTargetResolver
{
    /// <summary>Throws <see cref="ArgumentException"/> if the specification/operation/connection don't exist, or the operation can't be sent through that connection's service type — see <see cref="OperationCompatibility"/>.</summary>
    public static async Task<ResolvedTestScenarioTarget> ResolveAsync(
        IApiSpecificationRepository specifications,
        IConnectionRepository connections,
        Guid specificationId,
        Guid mockEndpointId,
        Guid connectionId,
        CancellationToken cancellationToken)
    {
        var specification = await specifications.FindByIdAsync(specificationId, cancellationToken)
            ?? throw new ArgumentException($"No specification with id '{specificationId}' exists.");

        var endpoint = specification.Endpoints.FirstOrDefault(e => e.Id == mockEndpointId)
            ?? throw new ArgumentException($"No operation with id '{mockEndpointId}' exists in specification '{specification.Title}'.");

        var connection = await connections.FindByIdAsync(connectionId, cancellationToken)
            ?? throw new ArgumentException($"No connection with id '{connectionId}' exists.");

        if (!OperationCompatibility.IsCompatible(endpoint.OperationKey, connection.ServiceType))
        {
            throw new ArgumentException($"Operation \"{endpoint.OperationKey}\" can't be sent through a {connection.ServiceType} connection.");
        }

        return new ResolvedTestScenarioTarget(specification, endpoint, connection);
    }

    /// <summary>
    /// Checks <paramref name="kind"/> against the resolved operation and normalizes its settings:
    /// the timeout is null (meaning "use the default") for a Listen scenario that gave none, and
    /// always null for a Send one; the broker options are checked against what the connection's
    /// type accepts (<see cref="BrokerOptionRules.Normalize"/> — either kind; blank means "use the
    /// default"). Throws <see cref="ArgumentException"/> on an unknown kind or option, an operation
    /// or connection that can't be listened on, or a timeout outside
    /// 1..<see cref="TestScenarioListening.MaxTimeoutSeconds"/>.
    /// </summary>
    public static (int? TimeoutSeconds, BrokerOptions? Options) ValidateKindSettings(
        IBrokerRules brokerRules,
        ResolvedTestScenarioTarget target,
        TestScenarioKind kind,
        int? timeoutSeconds,
        string? exchange,
        IReadOnlyDictionary<string, string?>? brokerOptions)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentException($"Unknown test scenario kind '{kind}'.");
        }

        var options = BrokerOptionRules.Normalize(brokerRules, target.Connection.ServiceType, exchange, brokerOptions);

        if (kind != TestScenarioKind.Listen)
        {
            return (null, options);
        }

        if (!TestScenarioListening.CanListen(target.Endpoint.OperationKey))
        {
            throw new ArgumentException(OperationCompatibility.IsHttpOperation(target.Endpoint.OperationKey)
                ? $"Operation \"{target.Endpoint.OperationKey}\" isn't a broker channel, so it can't be listened to."
                : $"Operation \"{target.Endpoint.OperationKey}\" has a channel parameter that is only part of a segment, so it can't be subscribed to.");
        }

        if (ServiceTypeTraits.Find(target.Connection.ServiceType) is { CanListen: false, ListenNote: var note })
        {
            throw new ArgumentException(note ?? $"A {target.Connection.ServiceType} connection can't be listened on.");
        }

        // CanListen above already found the operation's channel address, so it isn't null here.
        if (brokerRules.WhyCantListen(target.Connection.ServiceType, OperationCompatibility.ChannelAddressOf(target.Endpoint.OperationKey)!, options) is { } reason)
        {
            throw new ArgumentException(reason);
        }

        if (timeoutSeconds is < 1 or > TestScenarioListening.MaxTimeoutSeconds)
        {
            throw new ArgumentException($"The listen timeout must be between 1 and {TestScenarioListening.MaxTimeoutSeconds} seconds.");
        }

        return (timeoutSeconds, options);
    }
}
