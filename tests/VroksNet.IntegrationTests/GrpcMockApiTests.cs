using System.IO.Compression;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Net.Compression;
using VroksNet.IntegrationTests.Fixtures;
using VroksNet.IntegrationTests.Grpc.Generated;

namespace VroksNet.IntegrationTests;

/// <summary>
/// The gRPC mock (ADR 0004, plan step G4) over the wire: Protos/itest_orders.proto is imported
/// through the API, then a client Grpc.Tools generated from the same file calls apiservice's
/// "grpc" endpoint (h2c). The answer is the example built from the .proto; the call history keeps
/// each call with its gRPC status and contract outcome.
/// </summary>
[Collection("AppHost")]
public sealed class GrpcMockApiTests(AppHostFixture fixture) : IAsyncLifetime
{
    private const string GetOrder = "/itest.grpc.v1.Orders/GetOrder";
    private GrpcChannel _channel = null!;
    private Guid _getOrderId;

    public async ValueTask InitializeAsync()
    {
        var proto = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Protos", "itest_orders.proto"));
        using var form = new MultipartFormDataContent { { new StringContent(proto), "files", "itest_orders.proto" } };
        using var response = await fixture.ApiServiceClient.PostAsync("/api/specifications/proto", form);
        response.EnsureSuccessStatusCode();
        var id = (await response.Content.ReadFromJsonAsync<JsonNode>())!["specifications"]![0]!["id"]!.GetValue<Guid>();
        var details = (await fixture.ApiServiceClient.GetFromJsonAsync<JsonNode>($"/api/specifications/{id}"))!;
        _getOrderId = details["endpoints"]!.AsArray().Single(endpoint => endpoint!["operationKey"]!.GetValue<string>() == "RPC " + GetOrder)!["id"]!.GetValue<Guid>();

        _channel = GrpcChannel.ForAddress(fixture.GrpcAddress, new GrpcChannelOptions
        {
            CompressionProviders = [new GzipCompressionProvider(CompressionLevel.Fastest)]
        });
    }

    public ValueTask DisposeAsync()
    {
        _channel.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task AUnaryCall_AnswersWithTheExample_AndIsInTheHistory()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = new Orders.OrdersClient(_channel);

        var order = await client.GetOrderAsync(new GetOrderRequest { OrderId = "o-1" }, cancellationToken: cancellationToken);

        // Built from the schema: strings are "string", the timestamp is {{now}} filled in.
        Assert.Equal("string", order.Id);
        Assert.Equal(["string"], order.Items);
        Assert.InRange(order.CreatedAt.ToDateTimeOffset(), DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(5));

        var record = await LatestRecordAsync(cancellationToken);
        Assert.Equal(("InboundGrpcCall", "RPC " + GetOrder, 0, true), (record["direction"]!.GetValue<string>(), record["requestLine"]!.GetValue<string>(), record["statusCode"]!.GetValue<int>(), record["contractValid"]!.GetValue<bool>()));
    }

    [Fact]
    public async Task AGzipCompressedCall_IsAnswered()
    {
        var client = new Orders.OrdersClient(_channel);
        var headers = new Metadata { { "grpc-internal-encoding-request", "gzip" } };

        var order = await client.GetOrderAsync(new GetOrderRequest { OrderId = new string('x', 2000) }, headers, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("string", order.Id);
    }

    [Fact]
    public async Task BytesThatArentTheRequestMessage_AreStillAnswered_ButRecordedAsAViolation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // Field 1 (order_id, a string) sent as a varint.
        var response = await RawCallAsync(GetOrder, [0x08, 0x01], cancellationToken);

        Assert.NotEmpty(response);
        var record = await LatestRecordAsync(cancellationToken);
        Assert.False(record["contractValid"]!.GetValue<bool>());
    }

    [Fact]
    public async Task AnUnknownMethod_IsUnimplemented()
    {
        var error = await Assert.ThrowsAsync<RpcException>(() => RawCallAsync("/itest.grpc.v1.Orders/Nope", [], TestContext.Current.CancellationToken));

        Assert.Equal(StatusCode.Unimplemented, error.StatusCode);
        Assert.Contains("isn't mocked", error.Status.Detail);
    }

    [Fact]
    public async Task AStreamingMethod_IsUnimplementedForNow()
    {
        var client = new Orders.OrdersClient(_channel);
        using var call = client.WatchOrders(new GetOrderRequest(), cancellationToken: TestContext.Current.CancellationToken);

        var error = await Assert.ThrowsAsync<RpcException>(() => call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));

        Assert.Equal(StatusCode.Unimplemented, error.StatusCode);
    }

    [Fact]
    public async Task ATurnedOffMethod_IsUnimplemented_UntilTurnedBackOn()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = new Orders.OrdersClient(_channel);
        (await fixture.ApiServiceClient.PutAsJsonAsync($"/api/mock-endpoints/{_getOrderId}/enabled", new { enabled = false }, cancellationToken)).EnsureSuccessStatusCode();
        try
        {
            var error = await Assert.ThrowsAsync<RpcException>(async () => await client.GetOrderAsync(new GetOrderRequest(), cancellationToken: cancellationToken));
            Assert.Equal(StatusCode.Unimplemented, error.StatusCode);
            Assert.Contains("turned off", error.Status.Detail);
        }
        finally
        {
            (await fixture.ApiServiceClient.PutAsJsonAsync($"/api/mock-endpoints/{_getOrderId}/enabled", new { enabled = true }, cancellationToken)).EnsureSuccessStatusCode();
        }

        Assert.Equal("string", (await client.GetOrderAsync(new GetOrderRequest(), cancellationToken: cancellationToken)).Id);
    }

    /// <summary>A unary call with bytes of our own choosing — what a generated client can't send.</summary>
    private async Task<byte[]> RawCallAsync(string path, byte[] request, CancellationToken cancellationToken)
    {
        var bytes = Marshallers.Create(value => value, value => value);
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var method = new Method<byte[], byte[]>(MethodType.Unary, parts[0], parts[1], bytes, bytes);
        return await _channel.CreateCallInvoker().AsyncUnaryCall(method, null, new CallOptions(cancellationToken: cancellationToken), request);
    }

    private async Task<JsonNode> LatestRecordAsync(CancellationToken cancellationToken)
    {
        var page = (await fixture.ApiServiceClient.GetFromJsonAsync<JsonNode>("/api/call-records?direction=InboundGrpcCall&limit=1", cancellationToken))!;
        return page["items"]![0]!;
    }
}
