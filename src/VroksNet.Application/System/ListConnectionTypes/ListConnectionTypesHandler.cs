using Mediator;
using VroksNet.Application.Abstractions;
using VroksNet.Domain.Connections;

namespace VroksNet.Application.System.ListConnectionTypes;

public sealed class ListConnectionTypesHandler(IBrokerRules brokerRules) : IRequestHandler<ListConnectionTypes, IReadOnlyList<ConnectionTypeInfo>>
{
    public ValueTask<IReadOnlyList<ConnectionTypeInfo>> Handle(ListConnectionTypes request, CancellationToken cancellationToken) =>
        new(ServiceTypeTraits.All
            .Select(traits => new ConnectionTypeInfo(traits.Type, traits.DisplayName, traits.ValueLabel, traits.ValueHint, traits.IsHttp, traits.CanListen, traits.ListenNote, brokerRules.OptionsOf(traits.Type)))
            .ToList());
}
