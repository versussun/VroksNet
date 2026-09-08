using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace VroksNet.Application;

public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Application layer, including the mediator.
    /// </summary>
    /// <remarks>
    /// <c>AddMediator</c> must be called from this project (the one referencing
    /// <c>Mediator.SourceGenerator</c>) rather than from a downstream project like
    /// <c>VroksNet.ApiService</c> — the source generator only inspects calls made within its
    /// own compilation, so a call from another project produces generated code baked with the
    /// default (Singleton) lifetime regardless of what's requested there, causing a startup
    /// exception when the requested lifetime doesn't match.
    /// </remarks>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediator(options =>
        {
            options.ServiceLifetime = ServiceLifetime.Scoped;
        });

        return services;
    }
}
