using VroksNet.Domain.TestScenarios;

namespace VroksNet.UnitTests.TestScenarios;

public class ChannelPatternTests
{
    [Theory]
    [InlineData("orders.created", '.', "orders.created")]
    [InlineData("orders.{region}.created", '.', "orders.*.created")]
    [InlineData("{tenant}.orders.{id}", '.', "*.orders.*")]
    [InlineData("user/{userId}/signedup", '/', "user/*/signedup")]
    [InlineData("v1/orders.{region}.created", '.', "v1/orders.*.created")] // "." works, so it wins
    [InlineData("v1/orders/created", '.', "v1/orders/created")]            // no parameters: any separator renders it as-is
    public void Parse_FindsTheSeparatorAndParameters(string channelAddress, char separator, string rendered)
    {
        var pattern = ChannelPattern.Parse(channelAddress);

        Assert.NotNull(pattern);
        Assert.Equal(separator, pattern.Separator);
        Assert.Equal(rendered, pattern.Render("*"));
        Assert.Equal(channelAddress, pattern.Address);
    }

    [Theory]
    [InlineData("orders.eu-{region}.created")]
    [InlineData("user/{userId}-x/signedup")]
    [InlineData("a/{x}.{y}")]
    public void Parse_ParameterOnlyPartOfASegment_IsNull(string channelAddress)
        => Assert.Null(ChannelPattern.Parse(channelAddress));

    [Fact]
    public void HasParameters_OnlyWhenASegmentIsAParameter()
    {
        Assert.True(ChannelPattern.Parse("orders.{region}.created")!.HasParameters);
        Assert.False(ChannelPattern.Parse("orders.created")!.HasParameters);
    }
}
