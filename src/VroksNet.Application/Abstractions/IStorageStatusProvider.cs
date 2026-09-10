namespace VroksNet.Application.Abstractions;

/// <summary>
/// Which SQLite storage mode <c>InfrastructureServiceCollectionExtensions.AddInfrastructure</c>
/// picked at startup — for display only (the Settings page), not a runtime switch. Changing mode
/// means restarting the process with a different <c>ConnectionStrings:VroksNetDb</c>; nothing in
/// the running app can do that to itself.
/// </summary>
public interface IStorageStatusProvider
{
    bool IsInMemory { get; }

    /// <summary>The SQLite file path when not <see cref="IsInMemory"/>; null otherwise.</summary>
    string? FilePath { get; }
}
