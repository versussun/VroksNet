using VroksNet.Application.Abstractions;
using VroksNet.Application.Connections;
using VroksNet.Application.TestScenarios;
using VroksNet.Domain.Connections;
using VroksNet.Domain.Publishers;
using VroksNet.Domain.TestScenarios;

namespace VroksNet.Application.Publishers;

/// <summary>Shared by <c>CreatePublisherHandler</c>/<c>UpdatePublisherHandler</c> so both validate the same way.</summary>
internal static class PublisherRules
{
    /// <summary>
    /// Resolves what the publisher points at and checks it: a name no other publisher has, an AsyncAPI operation sent
    /// through a compatible broker connection, and an interval within <see cref="PublisherSchedule"/>'s
    /// bounds. Returns the broker options to store (<see cref="BrokerOptionRules.Normalize"/>). Throws
    /// <see cref="ArgumentException"/> on anything invalid.
    /// </summary>
    public static async Task<BrokerOptions?> ValidateAsync(
        IBrokerRules brokerRules,
        IPublisherRepository publishers,
        IApiSpecificationRepository specifications,
        IConnectionRepository connections,
        Guid? publisherId,
        string name,
        Guid specificationId,
        Guid mockEndpointId,
        Guid connectionId,
        int intervalSeconds,
        string? exchange,
        IReadOnlyDictionary<string, string?>? brokerOptions,
        CancellationToken cancellationToken)
    {
        var normalized = UniqueNames.Normalize(name, "publisher");
        UniqueNames.EnsureFree((await publishers.FindByNameAsync(normalized, cancellationToken))?.Id, publisherId, "publisher", normalized);

        var target = await TestScenarioTargetResolver.ResolveAsync(specifications, connections, specificationId, mockEndpointId, connectionId, cancellationToken);
        if (OperationCompatibility.IsHttpOperation(target.Endpoint.OperationKey))
        {
            throw new ArgumentException($"Operation \"{target.Endpoint.OperationKey}\" is an HTTP operation — only AsyncAPI operations can be published to a broker.");
        }

        if (intervalSeconds is < PublisherSchedule.MinIntervalSeconds or > PublisherSchedule.MaxIntervalSeconds)
        {
            throw new ArgumentException($"The interval must be between {PublisherSchedule.MinIntervalSeconds} and {PublisherSchedule.MaxIntervalSeconds} seconds.");
        }

        return BrokerOptionRules.Normalize(brokerRules, target.Connection.ServiceType, exchange, brokerOptions);
    }
}
