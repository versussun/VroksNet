using Mediator;
using VroksNet.Application.Abstractions;

namespace VroksNet.Application.System.GetProviderInfo;

public sealed class GetProviderInfoHandler(IProviderSettings settings) : IRequestHandler<GetProviderInfo, ProviderInfo>
{
    public ValueTask<ProviderInfo> Handle(GetProviderInfo request, CancellationToken cancellationToken) =>
        new(new ProviderInfo(settings.Port is not null, settings.Port, settings.PublicUrl));
}
