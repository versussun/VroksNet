using VroksNet.Application.Abstractions;

namespace VroksNet.Infrastructure.Persistence;

/// <summary>Plain snapshot of the storage-mode decision <c>AddInfrastructure</c> made at startup — see <see cref="IStorageStatusProvider"/>.</summary>
public sealed class StorageStatusProvider(bool isInMemory, string? filePath) : IStorageStatusProvider
{
    public bool IsInMemory { get; } = isInMemory;

    public string? FilePath { get; } = filePath;
}
