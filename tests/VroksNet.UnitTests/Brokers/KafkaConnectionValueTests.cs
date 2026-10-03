using VroksNet.Infrastructure.Brokers.Kafka;

namespace VroksNet.UnitTests.Brokers;

/// <summary>
/// What a Kafka connection's value turns into (<see cref="KafkaClients.ConfigFrom"/>). Event Hubs
/// (N1 of the broker adapters plan) needs a SASL password that is itself a connection string full
/// of ";" and "=", so values can be quoted, and an Event Hubs connection string can be pasted as is.
/// Verified against the Event Hubs emulator while adding this.
/// </summary>
public class KafkaConnectionValueTests
{
    private const string EmulatorConnectionString = "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";

    [Fact]
    public void BootstrapServersList_IsTheBootstrapServers()
        => Assert.Equal(new Dictionary<string, string> { ["bootstrap.servers"] = "host:9092,host2:9092" }, KafkaClients.ConfigFrom(" host:9092,host2:9092 "));

    [Fact]
    public void KeyValueSettings_AreTakenAsTheyAre()
    {
        var config = KafkaClients.ConfigFrom("bootstrap.servers=host:9093; security.protocol=SASL_SSL ;sasl.mechanism=PLAIN;");

        Assert.Equal(("host:9093", "SASL_SSL", "PLAIN"), (config!["bootstrap.servers"], config["security.protocol"], config["sasl.mechanism"]));
    }

    [Fact]
    public void QuotedValue_MayContainSemicolonsEqualsAndQuotes()
    {
        var config = KafkaClients.ConfigFrom($"bootstrap.servers=localhost:9092;sasl.password=\"{EmulatorConnectionString}\";client.id=\"say \"\"hi\"\"\"");

        Assert.Equal(EmulatorConnectionString, config!["sasl.password"]);
        Assert.Equal("say \"hi\"", config["client.id"]);
    }

    [Theory]
    [InlineData("client.id=no-servers")]                           // no bootstrap.servers
    [InlineData("bootstrap.servers=host:9092;oops")]               // a pair without "="
    [InlineData("bootstrap.servers=host:9092;=value")]             // a pair without a key
    [InlineData("bootstrap.servers=host:9092;sasl.password=\"open")] // unterminated quote
    [InlineData("bootstrap.servers=host:9092;sasl.password=\"a\"b")] // something after the closing quote
    [InlineData("Endpoint=not-a-uri;SharedAccessKey=x")]           // an Event Hubs string without a host
    public void Malformed_IsNull(string value)
        => Assert.Null(KafkaClients.ConfigFrom(value));

    [Fact]
    public void EventHubsConnectionString_UsesTheNamespacesKafkaEndpoint()
    {
        const string connectionString = "Endpoint=sb://shop.servicebus.windows.net/;SharedAccessKeyName=send;SharedAccessKey=abc=";

        var config = KafkaClients.ConfigFrom(connectionString)!;

        Assert.Equal("shop.servicebus.windows.net:9093", config["bootstrap.servers"]);
        Assert.Equal(("SASL_SSL", "PLAIN", "$ConnectionString"), (config["security.protocol"], config["sasl.mechanism"], config["sasl.username"]));
        Assert.Equal(connectionString, config["sasl.password"]);
    }

    [Fact]
    public void EventHubsEmulatorConnectionString_UsesPlaintextOn9092()
    {
        var config = KafkaClients.ConfigFrom(EmulatorConnectionString)!;

        Assert.Equal("localhost:9092", config["bootstrap.servers"]);
        Assert.Equal("SASL_PLAINTEXT", config["security.protocol"]);
        Assert.Equal(EmulatorConnectionString, config["sasl.password"]);
    }
}
