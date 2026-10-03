using Mediator;
using VroksNet.Application.Abstractions;
using VroksNet.Application.Provisioning;

namespace VroksNet.Application.System.GetSystemInfo;

public sealed class GetSystemInfoHandler(IAppVersionProvider version, ProvisioningState provisioning) : IRequestHandler<GetSystemInfo, SystemInfo>
{
    public ValueTask<SystemInfo> Handle(GetSystemInfo request, CancellationToken cancellationToken) =>
        new(new SystemInfo(version.Version, ContainerContract.Version, provisioning.Current));
}
