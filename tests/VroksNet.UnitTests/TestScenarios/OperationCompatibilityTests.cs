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
    public void IsCompatible_MatchesOperationShapeToConnectionType(string operationKey, ConnectionServiceType serviceType, bool expected)
    {
        Assert.Equal(expected, OperationCompatibility.IsCompatible(operationKey, serviceType));
    }

    [Theory]
    [InlineData("GET /pets")]
    [InlineData("orders.created:send")]
    public void IsCompatible_UnknownServiceType_IsFalse(string operationKey)
        => Assert.False(OperationCompatibility.IsCompatible(operationKey, (ConnectionServiceType)99));
}
