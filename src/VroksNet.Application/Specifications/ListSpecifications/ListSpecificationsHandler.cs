using Mediator;
using VroksNet.Application.Abstractions;

namespace VroksNet.Application.Specifications.ListSpecifications;

public sealed class ListSpecificationsHandler(IApiSpecificationRepository repository)
    : IRequestHandler<ListSpecifications, IReadOnlyList<SpecificationSummary>>
{
    public async ValueTask<IReadOnlyList<SpecificationSummary>> Handle(ListSpecifications request, CancellationToken cancellationToken)
    {
        var specifications = await repository.ListAsync(cancellationToken);

        return specifications
            .Select(specification => new SpecificationSummary(
                specification.Id,
                specification.Title,
                specification.Kind,
                specification.Endpoints.Count,
                specification.UpdatedAt))
            .OrderBy(summary => summary.Title)
            .ToList();
    }
}
