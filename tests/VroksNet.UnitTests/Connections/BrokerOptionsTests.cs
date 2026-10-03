using VroksNet.Domain.Connections;

namespace VroksNet.UnitTests.Connections;

public class BrokerOptionsTests
{
    [Fact]
    public void From_TrimsAndDropsBlankValues()
    {
        var options = BrokerOptions.From(new Dictionary<string, string?> { [" exchange "] = " orders ", ["qos"] = " ", ["retain"] = null });

        Assert.Equal(new Dictionary<string, string> { ["exchange"] = "orders" }, options!.Values);
    }

    [Fact]
    public void From_NothingLeft_IsNull()
    {
        Assert.Null(BrokerOptions.From(null));
        Assert.Null(BrokerOptions.From(new Dictionary<string, string?> { ["exchange"] = "" }));
    }

    [Fact]
    public void Without_RemovesOneOption_AndIsNullWhenNoneAreLeft()
    {
        var options = BrokerOptions.From(new Dictionary<string, string?> { ["exchange"] = "orders", ["qos"] = "1" })!;

        Assert.Equal(["qos"], options.Without("exchange")!.Values.Keys);
        Assert.Null(options.Without("exchange")!.Without("qos"));
    }

    [Fact]
    public void Equals_ComparesContentRegardlessOfOrder()
    {
        var left = BrokerOptions.From(new Dictionary<string, string?> { ["a"] = "1", ["b"] = "2" });
        var right = BrokerOptions.From(new Dictionary<string, string?> { ["b"] = "2", ["a"] = "1" });

        Assert.Equal(left, right);
        Assert.Equal(left!.GetHashCode(), right!.GetHashCode());
        Assert.NotEqual(left, BrokerOptions.From(new Dictionary<string, string?> { ["a"] = "1" }));
    }
}
