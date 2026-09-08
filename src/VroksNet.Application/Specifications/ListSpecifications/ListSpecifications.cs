using Mediator;

namespace VroksNet.Application.Specifications.ListSpecifications;

public sealed record ListSpecifications : IRequest<IReadOnlyList<SpecificationSummary>>;
