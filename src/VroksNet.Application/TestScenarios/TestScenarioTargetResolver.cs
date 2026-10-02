using VroksNet.Application.Abstractions;
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
    /// Checks <paramref name="kind"/> against the resolved operation and normalizes the listen
    /// settings: null (meaning "use the default") for a Listen scenario that gave none, and always
    /// null for a Send one. Throws <see cref="ArgumentException"/> on an operation that can't be
    /// listened to, an unknown kind, or a timeout outside 1..<see cref="TestScenarioListening.MaxTimeoutSeconds"/>.
    /// The exchange is kept only for a RabbitMQ connection.
    /// </summary>
    public static (int? TimeoutSeconds, string? Exchange) ValidateListenSettings(
        ResolvedTestScenarioTarget target,
        TestScenarioKind kind,
        int? timeoutSeconds,
        string? exchange)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentException($"Unknown test scenario kind '{kind}'.");
        }

        if (kind != TestScenarioKind.Listen)
        {
            return (null, null);
        }

        if (!TestScenarioListening.CanListen(target.Endpoint.OperationKey))
        {
            throw new ArgumentException(OperationCompatibility.IsHttpOperation(target.Endpoint.OperationKey)
                ? $"Operation \"{target.Endpoint.OperationKey}\" isn't a broker channel, so it can't be listened to."
                : $"Operation \"{target.Endpoint.OperationKey}\" has a channel parameter that isn't a whole \".\"-separated segment, so it can't be subscribed to.");
        }

        if (timeoutSeconds is < 1 or > TestScenarioListening.MaxTimeoutSeconds)
        {
            throw new ArgumentException($"The listen timeout must be between 1 and {TestScenarioListening.MaxTimeoutSeconds} seconds.");
        }

        // The exchange only means something for RabbitMQ — don't keep a stale one on a NATS scenario.
        var keptExchange = target.Connection.ServiceType == ConnectionServiceType.RabbitMq && !string.IsNullOrWhiteSpace(exchange)
            ? exchange.Trim()
            : null;

        return (timeoutSeconds, keptExchange);
    }
}
