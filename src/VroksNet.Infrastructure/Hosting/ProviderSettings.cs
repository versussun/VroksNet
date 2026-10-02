using Microsoft.Extensions.Configuration;
using VroksNet.Application.Abstractions;

namespace VroksNet.Infrastructure.Hosting;

public sealed class ProviderSettings(IConfiguration configuration) : IProviderSettings
{
    public const string CorsOriginsConfigKey = "Provider:CorsOrigins";

    public int? Port { get; } = configuration.GetValue<int?>("Provider:Port");

    public string? PublicUrl { get; } = configuration["Provider:PublicUrl"] is { Length: > 0 } url ? url.TrimEnd('/') : null;

    public IReadOnlyList<string> CorsOrigins { get; } = CorsOriginsFrom(configuration);

    /// <summary>
    /// <c>Provider:CorsOrigins</c> as a list — comma-separated origins, or <c>*</c> for any (which
    /// then stands alone). Empty when it isn't set: CORS stays off on the provider port. Trailing
    /// slashes are dropped, since a browser's Origin header never has one.
    /// </summary>
    public static IReadOnlyList<string> CorsOriginsFrom(IConfiguration configuration)
    {
        var origins = (configuration[CorsOriginsConfigKey] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(origin => origin.TrimEnd('/'))
            .Where(origin => origin.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return origins.Contains("*") ? ["*"] : origins;
    }
}
