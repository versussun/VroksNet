using VroksNet.Application.Provisioning;

namespace VroksNet.Application.Abstractions;

/// <summary>Records that provisioning brought an object in line with the manifest (its <c>ProvisionedAt</c>).</summary>
public interface IProvisionedMarker
{
    Task MarkAsync(ProvisionedObject kind, Guid id, DateTimeOffset at, CancellationToken cancellationToken);
}
