using Mediator;
using VroksNet.Application.Abstractions;

namespace VroksNet.Application.System.GetStorageStatus;

public sealed class GetStorageStatusHandler(IStorageStatusProvider storageStatus) : IRequestHandler<GetStorageStatus, StorageStatusResult>
{
    public ValueTask<StorageStatusResult> Handle(GetStorageStatus request, CancellationToken cancellationToken) =>
        new(new StorageStatusResult(storageStatus.IsInMemory, storageStatus.FilePath));
}
