using Grpc.Core;
using VroksNet.Application.Abstractions;
using VroksNet.Domain.Connections;
using VroksNet.Infrastructure.Brokers.Grpc;
using VroksNet.Infrastructure.Protobuf;
using VroksNet.Infrastructure.Specifications.Proto;

namespace VroksNet.UnitTests.Brokers;

/// <summary>
/// <see cref="GrpcBrokerAdapter"/> against values it refuses and targets nothing answers on
/// (ADR 0004). The success path — a call to VroksNet's own gRPC mock — is in the integration tests.
/// </summary>
public sealed class GrpcBrokerAdapterTests
{
    private const string ClosedPort = "http://127.0.0.1:1";
    private const string Say = "RPC /send.v1.Echo/Say";

    private readonly GrpcBrokerAdapter _adapter = new(new ProtobufTranscoder());

    [Theory]
    [InlineData("localhost:5001", "Not a valid http:// or https:// URL.")]
    [InlineData("grpc://localhost:5001", "Not a valid http:// or https:// URL.")]
    [InlineData("http://localhost:5001/api", "A gRPC URL has no path or query")]
    public async Task AValueThatIsntAServerUrl_IsRefused(string value, string expected)
    {
        var connection = Connection(value);

        var test = await _adapter.TestAsync(connection, TestContext.Current.CancellationToken);
        var send = await _adapter.SendAsync(connection, Say, "{}", null, TestContext.Current.CancellationToken, Context());

        Assert.Equal((false, false), (test.Success, send.Success));
        Assert.StartsWith(expected, test.Message);
        Assert.StartsWith(expected, send.Message);
    }

    [Fact]
    public async Task Test_NothingListening_FailsReadably()
    {
        var result = await _adapter.TestAsync(Connection(ClosedPort), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("refused", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Send_NothingListening_FailsWithoutAStatus()
    {
        var result = await _adapter.SendAsync(Connection(ClosedPort), Say, "{}", null, TestContext.Current.CancellationToken, Context());

        Assert.False(result.Success);
        Assert.Null(result.GrpcStatus);
        Assert.Contains("refused", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Send_APayloadThatIsntTheRequestMessage_FailsBeforeConnecting()
    {
        var result = await _adapter.SendAsync(Connection(ClosedPort), Say, """{ "text": 5 }""", null, TestContext.Current.CancellationToken, Context());

        Assert.False(result.Success);
        Assert.StartsWith("The payload isn't a valid send.v1.SayRequest: text: expected a string", result.Message);
    }

    [Fact]
    public async Task Send_WithoutTheMethodsDescriptors_Fails()
    {
        var result = await _adapter.SendAsync(Connection(ClosedPort), Say, "{}", null, TestContext.Current.CancellationToken);

        Assert.Equal((false, "The method's message types are unknown — import its .proto file again."), (result.Success, result.Message));
    }

    [Fact]
    public async Task Send_AnOperationThatIsntAGrpcMethod_Fails()
    {
        var result = await _adapter.SendAsync(Connection(ClosedPort), "GET /pets", null, null, TestContext.Current.CancellationToken, Context());

        Assert.False(result.Success);
        Assert.Contains("isn't a gRPC method", result.Message);
    }

    [Theory]
    [InlineData(StatusCode.OK, "OK")]
    [InlineData(StatusCode.NotFound, "NOT_FOUND")]
    [InlineData(StatusCode.DeadlineExceeded, "DEADLINE_EXCEEDED")]
    [InlineData(StatusCode.Unauthenticated, "UNAUTHENTICATED")]
    public void AStatus_IsNamedAsGrpcSpellsIt(StatusCode status, string expected)
        => Assert.Equal(expected, GrpcBrokerAdapter.NameOf(status));

    private static Connection Connection(string value)
        => new() { Id = Guid.NewGuid(), Name = "Echo", ServiceType = ConnectionServiceType.Grpc, Value = value };

    private static RpcSendContext Context()
    {
        var parsed = new ProtoSpecificationParser().Parse(new Dictionary<string, string>
        {
            ["echo.proto"] = """
                syntax = "proto3";
                package send.v1;
                service Echo { rpc Say (SayRequest) returns (SayReply); }
                message SayRequest { string text = 1; }
                message SayReply { string text = 1; }
                """
        });
        return new RpcSendContext(parsed.Specifications.Single().DescriptorSet, new RpcMethodTypes("send.v1.SayRequest", "send.v1.SayReply"));
    }
}
