using Mediator;

namespace VroksNet.Application.System.ListConnectionTypes;

public sealed record ListConnectionTypes : IRequest<IReadOnlyList<ConnectionTypeInfo>>;
