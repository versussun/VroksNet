namespace VroksNet.Application.System.GetStorageStatus;

public sealed record StorageStatusResult(bool IsInMemory, string? FilePath);
