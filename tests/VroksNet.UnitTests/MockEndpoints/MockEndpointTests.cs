using VroksNet.Domain.MockEndpoints;

namespace VroksNet.UnitTests.MockEndpoints;

public class MockEndpointTests
{
    private static readonly MockEndpoint Endpoint = new()
    {
        OperationKey = "GET /pets/{id}",
        ResponseSchemasByStatus = new Dictionary<string, string?>
        {
            ["200"] = "ok-schema",
            ["404"] = "not-found-schema",
            ["4XX"] = "client-error-schema",
            ["default"] = "default-schema"
        }
    };

    [Theory]
    [InlineData(200, "ok-schema")]       // exact code
    [InlineData(404, "not-found-schema")] // exact code wins over its range
    [InlineData(409, "client-error-schema")] // range
    [InlineData(503, "default-schema")]  // nothing closer — falls through to "default"
    public void TryGetDeclaredResponse_UsesExactThenRangeThenDefault(int statusCode, string expectedSchema)
    {
        Assert.True(Endpoint.TryGetDeclaredResponse(statusCode, out var schema));
        Assert.Equal(expectedSchema, schema);
    }

    [Fact]
    public void TryGetDeclaredResponse_NothingCoversTheStatus_ReturnsFalse()
    {
        var endpoint = new MockEndpoint { ResponseSchemasByStatus = new Dictionary<string, string?> { ["200"] = null } };

        Assert.False(endpoint.TryGetDeclaredResponse(500, out var schema));
        Assert.Null(schema);
    }

    [Fact]
    public void TryGetDeclaredResponse_DeclaredWithoutBody_ReturnsTrueWithNullSchema()
    {
        var endpoint = new MockEndpoint { ResponseSchemasByStatus = new Dictionary<string, string?> { ["204"] = null } };

        Assert.True(endpoint.TryGetDeclaredResponse(204, out var schema));
        Assert.Null(schema);
    }
}
