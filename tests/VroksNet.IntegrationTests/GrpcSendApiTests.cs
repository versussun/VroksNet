using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using VroksNet.IntegrationTests.Fixtures;

namespace VroksNet.IntegrationTests;

/// <summary>
/// Send through a Grpc connection (ADR 0004, plan step G5) over the wire, with VroksNet's own gRPC
/// mock as the service under test: Protos/itest_orders.proto is imported, a Grpc connection points
/// at apiservice's "grpc" endpoint, and a Test Scenario calls GetOrder through it. The mock answers
/// with its example, which the run decodes and checks against the response message.
/// </summary>
[Collection("AppHost")]
public sealed class GrpcSendApiTests(AppHostFixture fixture)
{
    private const string GetOrder = "RPC /itest.grpc.v1.Orders/GetOrder";

    /// <summary>
    /// The same method as itest_orders.proto, answering a different message: field 1 is an int64
    /// here, a string there. The extra service gives it a title of its own, so it's imported next to
    /// the original instead of replacing it — and its GetOrder comes in turned off, since the
    /// original serves the method.
    /// </summary>
    private const string MismatchedProto = """
        syntax = "proto3";
        package itest.grpc.v1;
        service Orders { rpc GetOrder (GetOrderRequest) returns (OrderSummary); }
        service Mismatched { rpc Ping (GetOrderRequest) returns (OrderSummary); }
        message GetOrderRequest { string order_id = 1; }
        message OrderSummary { int64 id = 1; }
        """;

    [Fact]
    public async Task ACallToTheGrpcMock_Passes_AndIsInTheHistory()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var proto = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Protos", "itest_orders.proto"), cancellationToken);
        var getOrder = await ImportAsync("itest_orders.proto", proto, cancellationToken);
        var connectionId = await CreateConnectionAsync(cancellationToken);
        var scenarioId = await CreateScenarioAsync(getOrder, connectionId, """{ "orderId": "o-1" }""", cancellationToken);

        var result = await RunAsync(scenarioId, cancellationToken);

        Assert.True(result["success"]!.GetValue<bool>(), result["message"]!.GetValue<string>());
        Assert.Equal("OK", result["message"]!.GetValue<string>());
        Assert.True(result["contractValidation"]!["isValid"]!.GetValue<bool>());
        Assert.Contains("\"id\": \"string\"", result["responseBody"]!.GetValue<string>());

        var page = (await fixture.ApiServiceClient.GetFromJsonAsync<JsonNode>($"/api/call-records?direction=OutboundGrpcCall&limit=1", cancellationToken))!;
        var record = page["items"]![0]!;
        Assert.Equal((scenarioId, 0, true), (record["testScenarioId"]!.GetValue<Guid>(), record["statusCode"]!.GetValue<int>(), record["contractValid"]!.GetValue<bool>()));
    }

    [Fact]
    public async Task AnAnswerThatIsntTheDeclaredMessage_IsAViolation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var proto = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Protos", "itest_orders.proto"), cancellationToken);
        await ImportAsync("itest_orders.proto", proto, cancellationToken);
        var mismatched = await ImportAsync("itest_mismatched.proto", MismatchedProto, cancellationToken);
        var connectionId = await CreateConnectionAsync(cancellationToken);
        var scenarioId = await CreateScenarioAsync(mismatched, connectionId, null, cancellationToken);

        var result = await RunAsync(scenarioId, cancellationToken);

        Assert.False(result["success"]!.GetValue<bool>());
        Assert.False(result["contractValidation"]!["isValid"]!.GetValue<bool>());
        Assert.Contains("isn't a valid itest.grpc.v1.OrderSummary", result["contractValidation"]!["errors"]![0]!.GetValue<string>());
    }

    [Fact]
    public async Task TheConnectionCheck_ReachesTheGrpcPort()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionId = await CreateConnectionAsync(cancellationToken);

        using var response = await fixture.ApiServiceClient.PostAsync($"/api/connections/{connectionId}/test", null, cancellationToken);
        var result = (await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!;

        // The mock doesn't serve grpc.health.v1, so it answers UNIMPLEMENTED — still proof it's reachable.
        Assert.True(result["success"]!.GetValue<bool>(), result["message"]!.GetValue<string>());
        Assert.Contains("UNIMPLEMENTED", result["message"]!.GetValue<string>());
    }

    /// <returns>The imported specification and its GetOrder operation.</returns>
    private async Task<(Guid SpecificationId, Guid EndpointId)> ImportAsync(string fileName, string proto, CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent { { new StringContent(proto), "files", fileName } };
        using var response = await fixture.ApiServiceClient.PostAsync("/api/specifications/proto", form, cancellationToken);
        response.EnsureSuccessStatusCode();
        var id = (await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!["specifications"]![0]!["id"]!.GetValue<Guid>();
        var details = (await fixture.ApiServiceClient.GetFromJsonAsync<JsonNode>($"/api/specifications/{id}", cancellationToken))!;
        return (id, details["endpoints"]!.AsArray().Single(endpoint => endpoint!["operationKey"]!.GetValue<string>() == GetOrder)!["id"]!.GetValue<Guid>());
    }

    private async Task<Guid> CreateConnectionAsync(CancellationToken cancellationToken)
    {
        // apiservice calls its own gRPC port, as any service under test would be called.
        using var response = await fixture.ApiServiceClient.PostAsJsonAsync(
            "/api/connections",
            new { Name = $"gRPC Send {Guid.NewGuid()}", ServiceType = "Grpc", Value = fixture.GrpcAddress.GetLeftPart(UriPartial.Authority) },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!["id"]!.GetValue<Guid>();
    }

    private async Task<Guid> CreateScenarioAsync((Guid SpecificationId, Guid EndpointId) operation, Guid connectionId, string? payload, CancellationToken cancellationToken)
    {
        using var response = await fixture.ApiServiceClient.PostAsJsonAsync(
            "/api/test-scenarios",
            new { Name = $"gRPC Send {Guid.NewGuid()}", operation.SpecificationId, MockEndpointId = operation.EndpointId, ConnectionId = connectionId, PayloadOverride = payload },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!["id"]!.GetValue<Guid>();
    }

    private async Task<JsonNode> RunAsync(Guid scenarioId, CancellationToken cancellationToken)
    {
        using var response = await fixture.ApiServiceClient.PostAsync($"/api/test-scenarios/{scenarioId}/run", null, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!;
    }
}
