using VroksNet.Domain.Connections;
using VroksNet.Domain.TestScenarios;

namespace VroksNet.UnitTests.TestScenarios;

public class OperationCompatibilityTests
{
    [Theory]
    [InlineData("GET /pets", ConnectionServiceType.Http, true)]
    [InlineData("POST /pets/{id}", ConnectionServiceType.Http, true)]
    [InlineData("GET /pets", ConnectionServiceType.RabbitMq, false)]
    [InlineData("GET /pets", ConnectionServiceType.Nats, false)]
    [InlineData("orders.created:send", ConnectionServiceType.RabbitMq, true)]
    [InlineData("orders.created:send", ConnectionServiceType.Nats, true)]
    [InlineData("orders.created:send", ConnectionServiceType.Http, false)]
    // No connection type carries gRPC calls yet (the Grpc type comes with its adapter, plan step G5).
    [InlineData("RPC /shop.orders.v1.Orders/GetOrder", ConnectionServiceType.Http, false)]
    [InlineData("RPC /shop.orders.v1.Orders/GetOrder", ConnectionServiceType.RabbitMq, false)]
    public void IsCompatible_MatchesOperationShapeToConnectionType(string operationKey, ConnectionServiceType serviceType, bool expected)
    {
        Assert.Equal(expected, OperationCompatibility.IsCompatible(operationKey, serviceType));
    }

    [Theory]
    [InlineData("GET /pets", OperationShape.Http)]
    [InlineData("DELETE /pets/{id}", OperationShape.Http)]
    [InlineData("orders.created:send", OperationShape.Message)]
    [InlineData("/user/signedup:receive", OperationShape.Message)]
    [InlineData("RPC /shop.orders.v1.Orders/GetOrder", OperationShape.Rpc)]
    [InlineData("RPC /Greeter/SayHello", OperationShape.Rpc)]
    public void ShapeOf_ReadsTheShapeFromTheKey(string operationKey, OperationShape expected)
        => Assert.Equal(expected, OperationCompatibility.ShapeOf(operationKey));

    [Fact]
    public void IsHttpOperation_RpcKey_IsFalse()
        => Assert.False(OperationCompatibility.IsHttpOperation("RPC /shop.orders.v1.Orders/GetOrder"));

    [Theory]
    [InlineData("orders.created:send", "orders.created")]
    [InlineData("GET /pets", null)]
    [InlineData("RPC /shop.orders.v1.Orders/GetOrder", null)]
    public void ChannelAddressOf_OnlyForAsyncApiKeys(string operationKey, string? expected)
        => Assert.Equal(expected, OperationCompatibility.ChannelAddressOf(operationKey));

    [Fact]
    public void CanListen_RpcKey_IsFalse()
        => Assert.False(TestScenarioListening.CanListen("RPC /shop.orders.v1.Orders/GetOrder"));

    [Theory]
    [InlineData("GET /pets")]
    [InlineData("orders.created:send")]
    [InlineData("RPC /shop.orders.v1.Orders/GetOrder")]
    public void IsCompatible_UnknownServiceType_IsFalse(string operationKey)
        => Assert.False(OperationCompatibility.IsCompatible(operationKey, (ConnectionServiceType)99));
}
