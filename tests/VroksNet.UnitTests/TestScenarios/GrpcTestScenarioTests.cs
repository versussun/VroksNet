using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using VroksNet.Application.Abstractions;
using VroksNet.Application.Specifications.ImportProtoSpecs;
using VroksNet.Application.TestScenarios.CreateTestScenario;
using VroksNet.Application.TestScenarios.RunTestScenario;
using VroksNet.Domain.CallRecords;
using VroksNet.Domain.Connections;
using VroksNet.Domain.TestScenarios;
using VroksNet.Infrastructure.Protobuf;
using VroksNet.Infrastructure.Scheduling;
using VroksNet.Infrastructure.SchemaValidation;
using VroksNet.Infrastructure.Specifications.Proto;
using VroksNet.UnitTests.TestDoubles;

namespace VroksNet.UnitTests.TestScenarios;

/// <summary>
/// A Send through a <see cref="ConnectionServiceType.Grpc"/> connection (ADR 0004, plan step G5),
/// with the real parser, transcoder and validator and a fake sender: what goes out, and how the
/// answer is judged — a status other than OK fails without a violation, response bytes are decoded
/// and checked against the response message.
/// </summary>
public sealed class GrpcTestScenarioTests
{
    private const string Proto = """
        syntax = "proto3";
        package send.v1;
        service Echo {
          rpc Say (SayRequest) returns (SayReply);
          rpc Stream (SayRequest) returns (stream SayReply);
        }
        message SayRequest { string text = 1; }
        message SayReply { string text = 1; Mood mood = 2; }
        enum Mood { MOOD_UNSPECIFIED = 0; HAPPY = 1; }
        """;

    private readonly FakeApiSpecificationRepository _specifications = new();
    private readonly FakeConnectionRepository _connections = new();
    private readonly FakeTestScenarioRepository _scenarios = new();
    private readonly FakeCallRecordRepository _callRecords = new();
    private readonly ProtobufTranscoder _transcoder = new();
    private byte[] _descriptorSet = [];

    [Fact]
    public async Task AnOkAnswer_ThatIsTheResponseMessage_Passes_AndIsRecordedWithItsStatus()
    {
        var scenarioId = await ArrangeAsync("Say");
        var sender = new FakeMessageSender(Ok(Reply("""{ "text": "hi", "mood": "HAPPY" }""")));

        var result = await RunAsync(scenarioId, sender);

        Assert.True(result.Success, result.Message);
        Assert.Equal(("OK", (int?)null, true), (result.Message, result.StatusCode, result.ContractValidation?.IsValid));
        Assert.Contains("\"mood\": \"HAPPY\"", result.ResponseBody);
        // No override: an empty request message, never the example (that's the response's shape).
        Assert.Equal("{}", sender.LastSend!.Value.Payload);
        Assert.Equal(new RpcMethodTypes("send.v1.SayRequest", "send.v1.SayReply"), sender.LastRpc!.Types);
        var record = Assert.Single(_callRecords.Inserted);
        Assert.Equal((CallDirection.OutboundGrpcCall, 0, true, "{}"), (record.Direction, record.StatusCode, record.ContractValid, record.RequestSnapshot));
    }

    [Fact]
    public async Task ThePayloadOverride_IsRenderedAndSent()
    {
        var scenarioId = await ArrangeAsync("Say", payloadOverride: """{ "text": "{{uuid}}" }""");
        var sender = new FakeMessageSender(Ok(Reply("{}")));

        await RunAsync(scenarioId, sender);

        Assert.DoesNotContain("{{uuid}}", sender.LastSend!.Value.Payload);
    }

    [Fact]
    public async Task BytesThatArentTheResponseMessage_AreAViolation()
    {
        var scenarioId = await ArrangeAsync("Say");
        // Field 1 (text, a string) as a varint.
        var result = await RunAsync(scenarioId, new FakeMessageSender(Ok([0x08, 0x01])));

        Assert.False(result.Success);
        Assert.Contains("isn't a valid send.v1.SayReply", Assert.Single(result.ContractValidation!.Errors));
        var record = Assert.Single(_callRecords.Inserted);
        Assert.Equal((false, "(2 bytes that aren't a valid send.v1.SayReply)"), (record.ContractValid, record.ResponseSnapshot));
    }

    [Fact]
    public async Task FieldsTheProtoDoesntDeclare_AreAWarning_NotAViolation()
    {
        var scenarioId = await ArrangeAsync("Say");

        var result = await RunAsync(scenarioId, new FakeMessageSender(Ok([.. Reply("""{ "text": "hi" }"""), 0x98, 0x06, 0x01])));

        Assert.True(result.Success, result.Message);
        Assert.Contains("field 99 isn't in the .proto", Assert.Single(JsonSerializer.Deserialize<string[]>(Assert.Single(_callRecords.Inserted).Warnings!)!));
    }

    [Fact]
    public async Task AStatusOtherThanOk_FailsTheRun_WithoutAViolation()
    {
        var scenarioId = await ArrangeAsync("Say");

        var result = await RunAsync(scenarioId, new FakeMessageSender(new MessageSendResult(false, "NOT_FOUND: no such order", GrpcStatus: 5)));

        Assert.False(result.Success);
        Assert.Equal(("NOT_FOUND: no such order", (object?)null), (result.Message, (object?)result.ContractValidation));
        var record = Assert.Single(_callRecords.Inserted);
        Assert.Equal((5, (bool?)null), (record.StatusCode, record.ContractValid));
    }

    [Fact]
    public async Task AStreamingMethod_FailsWithoutSending()
    {
        var scenarioId = await ArrangeAsync("Stream");
        var sender = new FakeMessageSender(Ok(Reply("{}")));

        var result = await RunAsync(scenarioId, sender);

        Assert.False(result.Success);
        Assert.Contains("streaming method", result.Message);
        Assert.Null(sender.LastSend);
    }

    [Fact]
    public async Task CreatingAScenario_ForAStreamingMethod_IsRefused()
    {
        await ImportAsync();
        var specification = (await _specifications.ListAsync(TestContext.Current.CancellationToken)).Single();
        var connection = await AddConnectionAsync();
        var handler = new CreateTestScenarioHandler(_scenarios, _specifications, _connections, new CronSchedule(), BrokerAdapters.Registry());

        var error = await Assert.ThrowsAsync<ArgumentException>(async () => await handler.Handle(
            new CreateTestScenario("Stream it", specification.Id, specification.Endpoints.Single(e => e.OperationKey.EndsWith("/Stream")).Id, connection.Id, null),
            TestContext.Current.CancellationToken));

        Assert.Contains("streaming method", error.Message);
    }

    private static MessageSendResult Ok(byte[] response) => new(true, "OK", GrpcStatus: 0, ResponseMessage: response);

    private byte[] Reply(string json) => _transcoder.ToBinary(_descriptorSet, "send.v1.SayReply", json).Bytes!;

    private async Task<Guid> ArrangeAsync(string method, string? payloadOverride = null)
    {
        await ImportAsync();
        var specification = (await _specifications.ListAsync(TestContext.Current.CancellationToken)).Single();
        var connection = await AddConnectionAsync();
        var scenario = new TestScenario
        {
            Id = Guid.NewGuid(),
            Name = $"Call {method}",
            SpecificationId = specification.Id,
            MockEndpointId = specification.Endpoints.Single(e => e.OperationKey == $"RPC /send.v1.Echo/{method}").Id,
            ConnectionId = connection.Id,
            PayloadOverride = payloadOverride
        };
        await _scenarios.InsertAsync(scenario, TestContext.Current.CancellationToken);
        return scenario.Id;
    }

    private async Task ImportAsync()
    {
        await new ImportProtoSpecsHandler(new ProtoSpecificationParser(), _specifications, TimeProvider.System, NullLogger<ImportProtoSpecsHandler>.Instance)
            .Handle(new ImportProtoSpecs(new Dictionary<string, string> { ["echo.proto"] = Proto }), TestContext.Current.CancellationToken);
        _descriptorSet = (await _specifications.ListAsync(TestContext.Current.CancellationToken)).Single().ProtoDescriptorSet!; // a .proto spec always has one
    }

    private async Task<Connection> AddConnectionAsync()
    {
        var connection = new Connection { Id = Guid.NewGuid(), Name = "Echo service", ServiceType = ConnectionServiceType.Grpc, Value = "http://localhost:5001" };
        await _connections.InsertAsync(connection, TestContext.Current.CancellationToken);
        return connection;
    }

    private async Task<RunTestScenarioResult> RunAsync(Guid scenarioId, FakeMessageSender sender)
        => await TestRunHandlers.Run(_scenarios, _specifications, _connections, sender, new FakeMessageListener(new MessageListenResult(false, "unused")), new SchemaValidator(), _callRecords)
            .Handle(new RunTestScenario(scenarioId), TestContext.Current.CancellationToken);
}
