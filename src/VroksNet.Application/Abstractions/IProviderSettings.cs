namespace VroksNet.Application.Abstractions;

/// <summary>
/// How provider mode's port is configured (<c>Provider:Port</c>, <c>Provider:PublicUrl</c>) — for
/// display only, like <see cref="IStorageStatusProvider"/>. <see cref="PublicUrl"/> is the address a
/// service under test should use, when the deployment knows it (AppHost sets it; behind Docker's
/// port mapping only the operator does).
/// </summary>
public interface IProviderSettings
{
    int? Port { get; }

    string? PublicUrl { get; }
}
