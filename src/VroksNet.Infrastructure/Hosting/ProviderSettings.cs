using Microsoft.Extensions.Configuration;
using VroksNet.Application.Abstractions;

namespace VroksNet.Infrastructure.Hosting;

public sealed class ProviderSettings(IConfiguration configuration) : IProviderSettings
{
    public int? Port { get; } = configuration.GetValue<int?>("Provider:Port");

    public string? PublicUrl { get; } = configuration["Provider:PublicUrl"] is { Length: > 0 } url ? url.TrimEnd('/') : null;
}
