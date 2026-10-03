using VroksNet.Domain.TestScenarios;
using VroksNet.Infrastructure.Brokers.Kafka;
using VroksNet.Infrastructure.Brokers.Nats;
using VroksNet.Infrastructure.Brokers.RabbitMq;

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

    private static ChannelPattern Pattern(string channelAddress)
        => ChannelPattern.Parse(channelAddress) ?? throw new ArgumentException($"\"{channelAddress}\" isn't a valid channel pattern.");
}
