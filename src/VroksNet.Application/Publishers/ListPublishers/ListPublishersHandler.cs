using Mediator;
using VroksNet.Application.Abstractions;

namespace VroksNet.Application.Publishers.ListPublishers;

public sealed class ListPublishersHandler(
    IPublisherRepository publishers,
    IApiSpecificationRepository specifications,
    IConnectionRepository connections) : IRequestHandler<ListPublishers, IReadOnlyList<PublisherSummary>>
{
    public async ValueTask<IReadOnlyList<PublisherSummary>> Handle(ListPublishers request, CancellationToken cancellationToken)
    {
        var all = await publishers.ListAsync(cancellationToken);
        if (all.Count == 0)
        {
            return [];
        }

        var specsById = (await specifications.ListAsync(cancellationToken)).ToDictionary(s => s.Id);
        var connectionsById = (await connections.ListAsync(cancellationToken)).ToDictionary(c => c.Id);

        return all
            .Select(publisher =>
            {
                var specification = specsById.GetValueOrDefault(publisher.SpecificationId);
                var endpoint = specification?.Endpoints.FirstOrDefault(e => e.Id == publisher.MockEndpointId);
                var connection = connectionsById.GetValueOrDefault(publisher.ConnectionId);
                return new PublisherSummary(
                    publisher.Id,
                    publisher.Name,
                    publisher.SpecificationId,
                    specification?.Title ?? "(deleted specification)",
                    publisher.MockEndpointId,
                    endpoint?.OperationKey ?? "(deleted operation)",
                    publisher.ConnectionId,
                    connection?.Name ?? "(deleted connection)",
                    connection?.ServiceType ?? default,
                    publisher.PayloadOverride,
                    publisher.Exchange,
                    publisher.IntervalSeconds,
                    publisher.IsEnabled,
                    publisher.UpdatedAt,
                    publisher.LastPublishedAt,
                    publisher.LastPublishSuccess,
                    publisher.LastPublishMessage);
            })
            .OrderBy(summary => summary.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
