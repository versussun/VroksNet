using Mediator;

namespace VroksNet.Application.Specifications.GetSpecificationDetails;

public sealed record GetSpecificationDetails(Guid Id) : IRequest<SpecificationDetails?>;
