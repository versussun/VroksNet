using Mediator;
using VroksNet.Application.Abstractions;

namespace VroksNet.Application.Publishers.UpdatePublisher;

/// <summary>Throws <see cref="ArgumentException"/> on invalid input — see <see cref="PublisherRules.ValidateAsync"/>.</summary>
public sealed class UpdatePublisherHandler(
    IPublisherRepository publishers,
    IApiSpecificationRepository specifications,
    IConnectionRepository connections,
    IBrokerRules brokerRules) : IRequestHandler<UpdatePublisher, bool>
{
    public async ValueTask<bool> Handle(UpdatePublisher request, CancellationToken cancellationToken)
    {
        var publisher = await publishers.FindByIdAsync(request.Id, cancellationToken);
        if (publisher is null)
        {
            return false;
        }

        var brokerOptions = await PublisherRules.ValidateAsync(
            brokerRules, publishers, specifications, connections, publisher.Id, request.Name, request.SpecificationId, request.MockEndpointId, request.ConnectionId,
            request.IntervalSeconds, request.Exchange, request.BrokerOptions, cancellationToken);

        publisher.Name = request.Name.Trim();
        publisher.SpecificationId = request.SpecificationId;
        publisher.MockEndpointId = request.MockEndpointId;
        publisher.ConnectionId = request.ConnectionId;
        publisher.PayloadOverride = string.IsNullOrWhiteSpace(request.PayloadOverride) ? null : request.PayloadOverride;
        publisher.BrokerOptions = brokerOptions;
        publisher.IntervalSeconds = request.IntervalSeconds;
        publisher.UpdatedAt = DateTimeOffset.UtcNow;

        return await publishers.UpdateAsync(publisher, cancellationToken);
    }
}
