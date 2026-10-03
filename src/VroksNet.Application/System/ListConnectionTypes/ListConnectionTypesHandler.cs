using Mediator;
using VroksNet.Domain.Connections;

namespace VroksNet.Application.System.ListConnectionTypes;

public sealed class ListConnectionTypesHandler : IRequestHandler<ListConnectionTypes, IReadOnlyList<ConnectionTypeInfo>>
{
    public ValueTask<IReadOnlyList<ConnectionTypeInfo>> Handle(ListConnectionTypes request, CancellationToken cancellationToken) =>
        new(ServiceTypeTraits.All
            .Select(traits => new ConnectionTypeInfo(traits.Type, traits.DisplayName, traits.ValueLabel, traits.ValueHint, traits.IsHttp, traits.CanListen, traits.ListenNote))
            .ToList());
}
