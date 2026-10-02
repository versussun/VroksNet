using VroksNet.Application.Mocking;

namespace VroksNet.UnitTests.Mocking;

public class OperationKeyMatcherTests
{
    [Theory]
    [InlineData("GET /pets", "GET", "/pets", true)]
    [InlineData("GET /pets", "get", "/pets", true)] // method compared case-insensitively
    [InlineData("GET /pets", "POST", "/pets", false)]
    [InlineData("GET /pets/{petId}", "GET", "/pets/1", true)]
    [InlineData("GET /pets/{petId}", "GET", "/pets/1/toys", false)] // segment count differs
    [InlineData("GET /pets/{petId}", "GET", "/pets", false)]
    [InlineData("GET /pets/{petId}/toys/{toyId}", "GET", "/pets/1/toys/9", true)]
    [InlineData("GET /pets", "GET", "/other", false)]
    public void Matches_ReturnsExpectedResult(string operationKey, string method, string path, bool expected)
    {
        Assert.Equal(expected, OperationKeyMatcher.Matches(operationKey, method, path));
    }

    [Fact]
    public void Matches_MalformedOperationKeyWithNoSpace_ReturnsFalse()
    {
        Assert.False(OperationKeyMatcher.Matches("GETpets", "GET", "/pets"));
    }

    [Fact]
    public void TryMatch_ReturnsEachParameterSegmentsValue()
    {
        Assert.True(OperationKeyMatcher.TryMatch("GET /pets/{petId}/toys/{toyId}", "GET", "/pets/1/toys/9", out var parameters));
        Assert.Equal(new Dictionary<string, string> { ["petId"] = "1", ["toyId"] = "9" }, parameters);
    }
}
