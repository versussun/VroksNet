using VroksNet.IntegrationTests.Fixtures;

namespace VroksNet.IntegrationTests.Brokers.Mqtt;

/// <summary>The graph with only the Mosquitto broker (AppHost.cs "mqtt") — the first broker family outside the core job (N2).</summary>
public sealed class MqttAppHostFixture() : AppHostFixtureBase(["mqtt"])
{
    /// <summary>The broker as an Mqtt connection value: <c>mqtt://localhost:&lt;port&gt;</c>.</summary>
    public string ConnectionValue
    {
        get
        {
            var endpoint = App.GetEndpoint("mqtt", "mqtt");
            return $"mqtt://{endpoint.Host}:{endpoint.Port}";
        }
    }
}
