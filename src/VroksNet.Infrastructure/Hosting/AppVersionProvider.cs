using System.Reflection;
using VroksNet.Application.Abstractions;

namespace VroksNet.Infrastructure.Hosting;

/// <summary>
/// The entry assembly's informational version (<c>-p:InformationalVersion=$VERSION</c> in the
/// Dockerfile), without the <c>+commit</c> suffix the SDK appends.
/// </summary>
public sealed class AppVersionProvider : IAppVersionProvider
{
    public string Version { get; } = Read(Assembly.GetEntryAssembly() ?? typeof(AppVersionProvider).Assembly);

    private static string Read(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
        var plus = informational.IndexOf('+');
        return plus < 0 ? informational : informational[..plus];
    }
}
