using VroksNet.Domain.TestScenarios;
using VroksNet.Infrastructure.Brokers.Kafka;
using VroksNet.Infrastructure.Brokers.Mqtt;
using VroksNet.Infrastructure.Brokers.Nats;
using VroksNet.Infrastructure.Brokers.RabbitMq;
using VroksNet.Infrastructure.Brokers.Redis;

namespace VroksNet.UnitTests.Brokers;

/// <summary>Each adapter renders a <see cref="ChannelPattern"/> in its own broker's wildcard syntax (ADR 0003).</summary>
public class SubscriptionSyntaxTests
{
    [Theory]
    [InlineData("orders.created", "orders.created")]
    [InlineData("orders.{region}.created", "orders.*.created")]
    [InlineData("{tenant}.orders.{id}", "*.orders.*")]
    [InlineData("v1/orders/created", "v1/orders/created")]
    [InlineData("user/{userId}/signedup", null)] // topic wildcards only stand for "."-separated words
    public void RabbitMq_BindingKey(string channelAddress, string? expected)
        => Assert.Equal(expected, RabbitMqBrokerAdapter.BindingKeyOf(Pattern(channelAddress)));

    [Theory]
    [InlineData("orders.created", "orders.created")]
    [InlineData("orders.{region}.created", "orders.*.created")]
    [InlineData("v1/orders/created", "v1/orders/created")]
    [InlineData("user/{userId}/signedup", null)] // NATS wildcards only stand for "."-separated tokens
    public void Nats_Subject(string channelAddress, string? expected)
        => Assert.Equal(expected, NatsBrokerAdapter.SubjectOf(Pattern(channelAddress)));

    [Theory]
    [InlineData("orders.created", "orders.created", true)]
    [InlineData("orders.created", "ordersXcreated", false)]   // "." is literal, not "any character"
    [InlineData("orders.{region}.created", "orders.eu.created", true)]
    [InlineData("orders.{region}.created", "orders.eu.west.created", false)] // one segment per parameter
    [InlineData("orders.{region}.created", "orders.created", false)]
    [InlineData("user/{userId}/signedup", "user/42/signedup", true)]
    [InlineData("user/{userId}/signedup", "user/4/2/signedup", false)]
    [InlineData("user/{userId}/signedup", "user/42/signedup.v2", false)]
    public void Kafka_TopicRegex(string channelAddress, string topic, bool matches)
        => Assert.Equal(matches, KafkaBrokerAdapter.TopicRegexOf(Pattern(channelAddress)).IsMatch(topic));

    [Theory]
    [InlineData("devices/{deviceId}/telemetry", "devices/+/telemetry")]
    [InlineData("{site}/{deviceId}", "+/+")]
    [InlineData("orders.created", "orders.created")]      // no parameters: the address as is
    [InlineData("orders.{region}.created", null)]          // "+" only stands for a whole "/"-separated level
    public void Mqtt_TopicFilter(string channelAddress, string? expected)
        => Assert.Equal(expected, MqttBrokerAdapter.TopicFilterOf(Pattern(channelAddress)));

    [Theory]
    [InlineData("orders.created", "orders.created", false)]           // no parameters: a plain SUBSCRIBE
    [InlineData("orders.{region}.created", "orders.*.created", true)]
    [InlineData("user/{userId}/signedup", "user/*/signedup", true)]   // any separator: a glob "*" isn't tied to one
    [InlineData("v[1]/{id}", @"v\[1\]/*", true)]                      // glob characters in literal segments are escaped
    public void Redis_Subscription(string channelAddress, string expected, bool isPattern)
    {
        var subscription = RedisBrokerAdapter.SubscriptionOf(Pattern(channelAddress));

        Assert.Equal((expected, isPattern), (subscription.ToString(), subscription.IsPattern));
    }

    [Theory]
    [InlineData("orders.{region}.created", "orders.eu.created", true)]
    [InlineData("orders.{region}.created", "orders.eu.west.created", false)] // a glob "*" matches this; the strict check doesn't
    [InlineData("user/{userId}/signedup", "user/42/signedup", true)]
    [InlineData("user/{userId}/signedup", "user//signedup", false)]
    [InlineData("orders.created", "ordersXcreated", false)]
    public void Redis_StrictMatch(string channelAddress, string channelName, bool matches)
        => Assert.Equal(matches, RedisBrokerAdapter.Matches(Pattern(channelAddress), channelName));

    private static ChannelPattern Pattern(string channelAddress)
        => ChannelPattern.Parse(channelAddress) ?? throw new ArgumentException($"\"{channelAddress}\" isn't a valid channel pattern.");
}
