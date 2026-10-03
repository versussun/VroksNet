using Microsoft.Extensions.DependencyInjection;
using VroksNet.Infrastructure;
using VroksNet.Infrastructure.Brokers;

namespace VroksNet.UnitTests.TestDoubles;

/// <summary>
/// The real <see cref="BrokerAdapterRegistry"/>, registered exactly as the app does it
/// (<see cref="InfrastructureServiceCollectionExtensions.AddBrokerAdapters"/>) — for tests of the
/// real ConnectionTester/MessageSender/MessageListener. Pass an <see cref="IHttpClientFactory"/> to
/// replace the one the Http adapter uses.
/// </summary>
public static class BrokerAdapters
{
    public static BrokerAdapterRegistry Registry(IHttpClientFactory? httpClientFactory = null)
    {
        var services = new ServiceCollection().AddBrokerAdapters();
        if (httpClientFactory is not null)
        {
            services.AddSingleton(httpClientFactory);
        }

        return services.BuildServiceProvider().GetRequiredService<BrokerAdapterRegistry>();
    }
}
