using Mediator;
using VroksNet.Application.Abstractions;
using VroksNet.Domain.Publishers;

namespace VroksNet.Application.Publishers.CreatePublisher;

/// <summary>Throws <see cref="ArgumentException"/> on invalid input — see <see cref="PublisherRules.ValidateAsync"/>.</summary>
public sealed class CreatePublisherHandler(
    IPublisherRepository publishers,
    IApiSpecificationRepository specifications,
    IConnectionRepository connections) : IRequestHandler<CreatePublisher, Guid>
{
    public async ValueTask<Guid> Handle(CreatePublisher request, CancellationToken cancellationToken)
    {
        var exchange = await PublisherRules.ValidateAsync(
            specifications, connections, request.Name, request.SpecificationId, request.MockEndpointId, request.ConnectionId,
            request.IntervalSeconds, request.Exchange, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var publisher = new Publisher
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            SpecificationId = request.SpecificationId,
            MockEndpointId = request.MockEndpointId,
            ConnectionId = request.ConnectionId,
            PayloadOverride = string.IsNullOrWhiteSpace(request.PayloadOverride) ? null : request.PayloadOverride,
            Exchange = exchange,
            IntervalSeconds = request.IntervalSeconds,
            IsEnabled = request.Enabled,
            CreatedAt = now,
            UpdatedAt = now
        };

        await publishers.InsertAsync(publisher, cancellationToken);
        return publisher.Id;
    }
}
