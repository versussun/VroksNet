namespace VroksNet.Application.Abstractions;

/// <summary>
/// The running app's version, as stamped at build time (the image's <c>VERSION</c> build arg,
/// e.g. <c>0.2.0</c>) — for display and for tooling that checks what it's talking to.
/// </summary>
public interface IAppVersionProvider
{
    string Version { get; }
}
