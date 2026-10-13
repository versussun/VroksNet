using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using VroksNet.Application.Mocking.InvokeRpcMock;
using VroksNet.Application.Specifications.ImportProtoSpecs;
using VroksNet.Domain.CallRecords;
using VroksNet.Infrastructure.Protobuf;
using VroksNet.Infrastructure.SchemaValidation;
using VroksNet.Infrastructure.Specifications.Proto;
using VroksNet.Infrastructure.Templating;
using VroksNet.UnitTests.TestDoubles;

namespace VroksNet.UnitTests.Mocking;

/// <summary>
/// <see cref="InvokeRpcMockHandler"/> (ADR 0004) with the real parser, transcoder, validator and
/// template engine over in-memory repositories: what the gRPC mock answers, and what it records.
/// </summary>
public sealed class InvokeRpcMockHandlerTests
{
    private const string Proto = """
        syntax = "proto3";
        package mock.v1;
        service Echo {
          rpc Say (SayRequest) returns (SayReply);
          rpc Stream (SayRequest) returns (stream SayReply);
        }
        message SayRequest { string text = 1; int32 times = 2; }
        message SayReply { string text = 1; string trace = 2; }
        """;

    private readonly FakeApiSpecificationRepository _specifications = new();
    private readonly FakeCallRecordRepository _callRecords = new();
    private readonly ProtobufTranscoder _transcoder = new();

    [Fact]
    public async Task AUnaryCall_AnswersWithTheRenderedExample_AndRecordsAValidCall()
    {
        await ImportAsync(reply: """{ "text": "{{request.body.text}}", "trace": "{{request.header.x-trace}}" }""");

        var result = await CallAsync("/mock.v1.Echo/Say", Request("""{ "text": "hi", "times": 2 }"""), new Dictionary<string, string> { ["x-trace"] = "t-1" });

        Assert.Equal(RpcStatus.Ok, result.Status);
        Assert.Contains("\"text\": \"hi\"", Decode("mock.v1.SayReply", result.Response!));
        Assert.Contains("\"trace\": \"t-1\"", Decode("mock.v1.SayReply", result.Response!));
        var record = Assert.Single(_callRecords.Inserted);
        Assert.Equal((CallDirection.InboundGrpcCall, "RPC /mock.v1.Echo/Say", 0, true), (record.Direction, record.RequestLine, record.StatusCode, record.ContractValid));
        Assert.Contains("\"times\": 2", record.RequestSnapshot);
    }

    [Fact]
    public async Task BytesThatArentTheRequest_AreStillAnswered_ButRecordedAsAViolation()
    {
        await ImportAsync();

        var result = await CallAsync("/mock.v1.Echo/Say", [0x08, 0x01]); // field 1 (a string) as a varint

        Assert.Equal(RpcStatus.Ok, result.Status);
        var record = Assert.Single(_callRecords.Inserted);
        Assert.False(record.ContractValid);
        Assert.Contains("isn't a valid mock.v1.SayRequest", Assert.Single(JsonSerializer.Deserialize<string[]>(record.ValidationErrors!)!));
        Assert.Contains("2 bytes that aren't", record.RequestSnapshot);
    }

    [Fact]
    public async Task FieldsTheProtoDoesntDeclare_AreAWarning_NotAViolation()
    {
        await ImportAsync();

        await CallAsync("/mock.v1.Echo/Say", [.. Request("""{ "text": "hi" }"""), 0x98, 0x06, 0x01]);

        var record = Assert.Single(_callRecords.Inserted);
        Assert.True(record.ContractValid);
        Assert.Contains("field 99 isn't in the .proto", Assert.Single(JsonSerializer.Deserialize<string[]>(record.Warnings!)!));
    }

    [Fact]
    public async Task AnUnknownMethod_IsUnimplemented_AndRecorded()
    {
        await ImportAsync();

        var result = await CallAsync("/mock.v1.Echo/Nope", []);

        Assert.Equal((RpcStatus.Unimplemented, "/mock.v1.Echo/Nope isn't mocked — import the .proto file that declares it."), (result.Status, result.Message));
        var record = Assert.Single(_callRecords.Inserted);
        Assert.Equal((12, (bool?)null, (Guid?)null), (record.StatusCode, record.ContractValid, record.MockEndpointId));
    }

    [Fact]
    public async Task ATurnedOffMethod_IsUnimplemented()
    {
        await ImportAsync();
        (await _specifications.ListAsync(TestContext.Current.CancellationToken)).Single().Endpoints.Single(e => e.OperationKey.EndsWith("/Say")).IsEnabled = false;

        var result = await CallAsync("/mock.v1.Echo/Say", Request("{}"));

        Assert.Equal((RpcStatus.Unimplemented, "/mock.v1.Echo/Say is mocked but turned off."), (result.Status, result.Message));
    }

    [Fact]
    public async Task AStreamingMethod_IsUnimplementedForNow()
    {
        await ImportAsync();

        var result = await CallAsync("/mock.v1.Echo/Stream", Request("{}"));

        Assert.Equal(RpcStatus.Unimplemented, result.Status);
        Assert.Contains("streaming method", result.Message);
    }

    [Fact]
    public async Task NotExactlyOneRequestMessage_IsInternal_AndAViolation()
    {
        await ImportAsync();

        var result = await new InvokeRpcMockHandler(_specifications, _callRecords, _transcoder, new SchemaValidator(), new ResponseTemplateEngine(TimeProvider.System), NullLogger<InvokeRpcMockHandler>.Instance)
            .Handle(new InvokeRpcMock("/mock.v1.Echo/Say", [Request("{}"), Request("{}")]), TestContext.Current.CancellationToken);

        Assert.Equal(RpcStatus.Internal, result.Status);
        Assert.False(Assert.Single(_callRecords.Inserted).ContractValid);
    }

    [Fact]
    public async Task AnExampleThatIsntTheResponseMessage_IsInternal()
    {
        await ImportAsync(reply: """{ "text": 5 }""");

        var result = await CallAsync("/mock.v1.Echo/Say", Request("{}"));

        Assert.Equal(RpcStatus.Internal, result.Status);
        Assert.Contains("The example isn't a valid mock.v1.SayReply: text: expected a string", result.Message);
    }

    [Fact]
    public async Task ImportingAMethodAnotherSpecServes_ComesInTurnedOff_WithANote()
    {
        await ImportAsync();

        var second = await new ImportProtoSpecsHandler(new ProtoSpecificationParser(), _specifications, TimeProvider.System, NullLogger<ImportProtoSpecsHandler>.Instance)
            .Handle(new ImportProtoSpecs(new Dictionary<string, string> { ["echo2.proto"] = Proto.Replace("service Echo {", "service Other { rpc Ping (SayRequest) returns (SayReply); }\nservice Echo {") }), TestContext.Current.CancellationToken);

        var imported = Assert.Single(second.Specifications);
        Assert.Contains("/mock.v1.Echo/Say is already served by \"mock.v1 (Echo)\", so it was imported turned off.", imported.Note);
        var stored = await _specifications.FindByIdAsync(imported.Id, TestContext.Current.CancellationToken);
        Assert.False(stored!.Endpoints.Single(e => e.OperationKey == "RPC /mock.v1.Echo/Say").IsEnabled);
        Assert.True(stored.Endpoints.Single(e => e.OperationKey == "RPC /mock.v1.Other/Ping").IsEnabled);
    }

    private async Task ImportAsync(string? reply = null)
    {
        await new ImportProtoSpecsHandler(new ProtoSpecificationParser(), _specifications, TimeProvider.System, NullLogger<ImportProtoSpecsHandler>.Instance)
            .Handle(new ImportProtoSpecs(new Dictionary<string, string> { ["echo.proto"] = Proto }), TestContext.Current.CancellationToken);
        var specification = (await _specifications.ListAsync(TestContext.Current.CancellationToken)).Single();
        _descriptorSet = specification.ProtoDescriptorSet!; // a .proto spec always has one
        if (reply is not null)
        {
            specification.Endpoints.Single(e => e.OperationKey == "RPC /mock.v1.Echo/Say").ExampleTemplate = reply;
        }
    }

    private byte[] _descriptorSet = [];

    private ValueTask<RpcMockResult> CallAsync(string path, byte[] message, IReadOnlyDictionary<string, string>? metadata = null)
        => new InvokeRpcMockHandler(_specifications, _callRecords, _transcoder, new SchemaValidator(), new ResponseTemplateEngine(TimeProvider.System), NullLogger<InvokeRpcMockHandler>.Instance)
            .Handle(new InvokeRpcMock(path, [message], metadata), TestContext.Current.CancellationToken);

    private byte[] Request(string json) => _transcoder.ToBinary(_descriptorSet, "mock.v1.SayRequest", json).Bytes!;

    private string Decode(string type, byte[] bytes) => _transcoder.ToJson(_descriptorSet, type, bytes).Json!;
}
