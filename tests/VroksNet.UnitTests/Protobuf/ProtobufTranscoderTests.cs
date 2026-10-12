using System.Text.Json.Nodes;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using VroksNet.Infrastructure.Protobuf;
using VroksNet.Infrastructure.Specifications.Proto;
using VroksNet.UnitTests.Protobuf.Generated;
using VroksNet.UnitTests.Protobuf.Generated.Legacy;
using Duration = Google.Protobuf.WellKnownTypes.Duration;
using Value = Google.Protobuf.WellKnownTypes.Value;

namespace VroksNet.UnitTests.Protobuf;

/// <summary>
/// <see cref="ProtobufTranscoder"/> against the generated-code oracle (ADR 0004): for each message,
/// the C# Grpc.Tools generated from Fixtures/proto gives the bytes (<c>ToByteArray</c>) and the
/// canonical JSON (<c>JsonFormatter</c>), and the transcoder — working only from the descriptor set
/// ProtoSpecificationParser stores for the same files — must produce the same in both directions.
/// </summary>
public sealed class ProtobufTranscoderTests
{
    private static readonly byte[] DescriptorSet = new ProtoSpecificationParser().Parse(Fixtures()).Specifications.Single().DescriptorSet;
    private static readonly TypeRegistry Registry = TypeRegistry.FromFiles(TranscodingReflection.Descriptor);
    private static readonly JsonFormatter Formatter = new(JsonFormatter.Settings.Default.WithTypeRegistry(Registry));
    private static readonly JsonParser Parser = new(JsonParser.Settings.Default.WithTypeRegistry(Registry));

    private readonly ProtobufTranscoder _transcoder = new();

    public static TheoryData<string, IMessage> Oracle() => new()
    {
        { "scalars", new Scalars
            {
                DoubleValue = 1.5, FloatValue = 2.25f, Int32Value = -42, Int64Value = long.MinValue, Uint32Value = uint.MaxValue,
                Uint64Value = ulong.MaxValue, Sint32Value = -7, Sint64Value = -9_000_000_000, Fixed32Value = 123, Fixed64Value = 1UL << 60,
                Sfixed32Value = -5, Sfixed64Value = long.MaxValue, BoolValue = true, StringValue = "héllo, мир ✓", BytesValue = ByteString.CopyFrom(0, 1, 254, 255),
                Color = Color.Green
            } },
        { "scalars at their defaults", new Scalars() },
        { "special floats", new Scalars { DoubleValue = double.NaN, FloatValue = float.NegativeInfinity } },
        { "tiny and huge doubles", new Scalars { DoubleValue = 1e-300, FloatValue = 3.4e38f } },
        { "a negative enum value", new Scalars { Color = Color.Negative } },
        { "an enum value the .proto doesn't name", new Scalars { Color = (Color)99 } },
        { "repeated", new Repeated
            {
                PackedInts = { 1, -1, 300, int.MinValue }, UnpackedInts = { 5, -5 }, Doubles = { 0.5, -0.0, 1e10 }, Sint64S = { -1, long.MaxValue },
                Fixed32S = { 0, 7 }, Bools = { true, false }, Colors = { Color.Red, Color.Negative }, Strings = { "", "a" },
                BytesList = { ByteString.Empty, ByteString.CopyFromUtf8("x") }, Messages = { new Scalars(), new Scalars { Int32Value = 1 } }
            } },
        { "maps", new Maps
            {
                StringToString = { ["a"] = "1", [""] = "", ["ключ"] = "значение" },
                IntToMessage = { [0] = new Scalars(), [-3] = new Scalars { StringValue = "x" } },
                BoolToInt64 = { [true] = 1, [false] = 0 },
                Uint64ToColor = { [ulong.MaxValue] = Color.Red, [0] = Color.Unspecified },
                Sint32ToBytes = { [-1] = ByteString.CopyFromUtf8("neg"), [2] = ByteString.Empty }
            } },
        { "optional fields set to their defaults", new Presence { OptionalInt = 0, OptionalString = "" } },
        { "a oneof string", new Presence { ChoiceString = "" } },
        { "a oneof int64", new Presence { ChoiceInt64 = 0 } },
        { "a oneof message", new Presence { ChoiceMessage = new Scalars() } },
        { "a oneof enum", new Presence { ChoiceColor = Color.Unspecified } },
        { "a message field and json_name", new Presence { Message = new Scalars { BoolValue = true }, Renamed = "custom" } },
        { "a recursive message", new Tree { Name = "root", Children = { new Tree { Name = "a", Children = { new Tree { Name = "a1" } } }, new Tree() } } },
        { "timestamps and durations", new WellKnown
            {
                Timestamp = new Timestamp { Seconds = 1_760_000_000, Nanos = 120_000_000 },
                Duration = new Duration { Seconds = -1, Nanos = -500_000_000 },
                Timestamps = { new Timestamp(), new Timestamp { Seconds = -62135596800 }, new Timestamp { Seconds = 253402300799, Nanos = 999_999_999 }, new Timestamp { Seconds = 1, Nanos = 1000 } }
            } },
        { "wrappers", new WellKnown
            {
                Int32Wrapper = 0, Int64Wrapper = long.MinValue, Uint64Wrapper = ulong.MaxValue, DoubleWrapper = double.PositiveInfinity, FloatWrapper = 0.5f,
                BoolWrapper = false, StringWrapper = "", BytesWrapper = ByteString.CopyFromUtf8("b"), Uint32Wrapper = 7
            } },
        { "struct, value and list", new WellKnown
            {
                Struct = new Struct { Fields = { ["n"] = Value.ForNumber(1.5), ["s"] = Value.ForString("x"), ["null"] = Value.ForNull(), ["nested"] = Value.ForStruct(new Struct { Fields = { ["b"] = Value.ForBool(true) } }), ["list"] = Value.ForList(Value.ForNumber(1), Value.ForString("two")) } },
                Value = Value.ForNull(),
                List = new ListValue { Values = { Value.ForBool(false), Value.ForStruct(new Struct()) } },
                Values = { ["a"] = Value.ForNumber(0), ["b"] = Value.ForList() }
            } },
        { "a field mask and empty", new WellKnown { FieldMask = new FieldMask { Paths = { "foo_bar", "baz.qux_quux" } }, Empty = new Empty() } },
        { "an Any of a message", new WellKnown { Any = Any.Pack(new Scalars { Int64Value = 5, Color = Color.Red }) } },
        { "an Any of a well-known type", new WellKnown { Any = Any.Pack(new Timestamp { Seconds = 60 }) } },
        { "proto2", new UsesLegacy { Legacy = new Legacy { Id = "", Count = 0, Unpacked = { 1, 2 }, Packed = { 3, 4 }, Kind = Legacy.Types.Kind.B } } },
        { "proto2 with only what's required", new UsesLegacy { Legacy = new Legacy { Id = "x" } } },
    };

    [Theory]
    [MemberData(nameof(Oracle))]
    public void ToBinary_FromCanonicalJson_MatchesTheGeneratedBytes(string name, IMessage message)
    {
        var encoded = _transcoder.ToBinary(DescriptorSet, message.Descriptor.FullName, Formatter.Format(message));

        Assert.True(encoded.Error is null, $"{name}: {encoded.Error}");
        Assert.Equal(message, message.Descriptor.Parser.ParseFrom(encoded.Bytes));
        Assert.Equal(message.ToByteArray(), encoded.Bytes);
    }

    [Theory]
    [MemberData(nameof(Oracle))]
    public void ToJson_FromTheGeneratedBytes_MatchesTheCanonicalJson(string name, IMessage message)
    {
        var decoded = _transcoder.ToJson(DescriptorSet, message.Descriptor.FullName, message.ToByteArray());

        Assert.True(decoded.Error is null, $"{name}: {decoded.Error}");
        Assert.Empty(decoded.Warnings);
        var expected = JsonNode.Parse(Formatter.Format(message));
        var actual = JsonNode.Parse(decoded.Json!);
        Assert.True(JsonNode.DeepEquals(expected, actual), $"{name}:\nexpected {expected?.ToJsonString()}\nactual   {actual?.ToJsonString()}");
        Assert.Equal(message, Parser.Parse(decoded.Json, message.Descriptor));
    }

    [Fact]
    public void ToBinary_AcceptsTheOtherFormsTheJsonMappingAllows()
    {
        var expected = new Scalars { Int64Value = 12, Uint64Value = 7, Int32Value = 3, Color = Color.Green, DoubleValue = 2.5, BytesValue = ByteString.CopyFrom(251, 255) };

        // Original field names, 64-bit integers as numbers, a 32-bit one as a string, an enum by number,
        // a float as a string, URL-safe base64 without padding.
        var encoded = _transcoder.ToBinary(DescriptorSet, "transcoding.v1.Scalars",
            """{ "int64_value": 12, "uint64Value": 7, "int32Value": "3", "color": 2, "double_value": "2.5", "bytesValue": "-_8" }""");

        Assert.True(encoded.Error is null, encoded.Error);
        Assert.Equal(expected.ToByteArray(), encoded.Bytes);
    }

    [Fact]
    public void ToBinary_NullMeansNotSet()
    {
        var encoded = _transcoder.ToBinary(DescriptorSet, "transcoding.v1.Presence", """{ "optionalInt": null, "message": null, "choiceString": null }""");

        Assert.Equal(Array.Empty<byte>(), encoded.Bytes);
    }

    [Theory]
    [InlineData("transcoding.v1.Scalars", """{ "surprise": 1 }""", "Scalars has no field \"surprise\"")]
    [InlineData("transcoding.v1.Scalars", """{ "int32Value": 1, "int32_value": 2 }""", "given twice")]
    [InlineData("transcoding.v1.Scalars", """{ "int32Value": 2147483648 }""", "int32Value: 2147483648 isn't an integer within")]
    [InlineData("transcoding.v1.Scalars", """{ "int32Value": 1.5 }""", "isn't an integer")]
    [InlineData("transcoding.v1.Scalars", """{ "color": "BLUE" }""", "color: \"BLUE\" isn't a value of Color")]
    [InlineData("transcoding.v1.Scalars", """{ "boolValue": "true" }""", "expected true or false")]
    [InlineData("transcoding.v1.Scalars", """{ "bytesValue": "!!" }""", "isn't valid base64")]
    [InlineData("transcoding.v1.Presence", """{ "choiceString": "a", "choiceInt64": "1" }""", "only one field of oneof choice")]
    [InlineData("transcoding.v1.Repeated", """{ "messages": [{ "int32Value": "x" }] }""", "messages[0].int32Value")]
    [InlineData("transcoding.v1.WellKnown", """{ "timestamp": "yesterday" }""", "timestamp: \"yesterday\" isn't an RFC 3339 timestamp")]
    [InlineData("transcoding.v1.WellKnown", """{ "duration": "5 minutes" }""", "isn't a duration")]
    [InlineData("transcoding.v1.WellKnown", """{ "any": { "@type": "type.googleapis.com/nope.Nope" } }""", "names a type the .proto files don't declare")]
    [InlineData("transcoding.v1.Maps", """{ "boolToInt64": { "yes": 1 } }""", "a bool map key must be")]
    [InlineData("transcoding.v1.Nope", "{}", "declare no message transcoding.v1.Nope")]
    [InlineData("transcoding.v1.Scalars", "{ not json", "Not valid JSON")]
    public void ToBinary_BadJson_IsAReadableError(string messageType, string json, string expected)
    {
        var encoded = _transcoder.ToBinary(DescriptorSet, messageType, json);

        Assert.Null(encoded.Bytes);
        Assert.Contains(expected, encoded.Error);
    }

    [Fact]
    public void ToJson_UnknownFields_AreSkippedWithAWarning()
    {
        // Field 1 (doubleValue) plus a field 99 the .proto doesn't declare.
        var bytes = new Scalars { StringValue = "kept" }.ToByteArray().Concat(new byte[] { 0x98, 0x06, 0x01 }).ToArray();

        var decoded = _transcoder.ToJson(DescriptorSet, "transcoding.v1.Scalars", bytes);

        Assert.Null(decoded.Error);
        Assert.Contains("\"stringValue\": \"kept\"", decoded.Json);
        Assert.Contains("field 99 isn't in the .proto", Assert.Single(decoded.Warnings));
    }

    [Fact]
    public void ToJson_TruncatedBytes_IsAnError()
    {
        var bytes = new Scalars { StringValue = "cut short" }.ToByteArray()[..^3];

        var decoded = _transcoder.ToJson(DescriptorSet, "transcoding.v1.Scalars", bytes);

        Assert.Null(decoded.Json);
        Assert.Contains("aren't a valid Scalars", decoded.Error);
    }

    [Fact]
    public void ToJson_AKnownFieldWithTheWrongWireType_IsAnError()
    {
        // Field 14 (stringValue) sent as a varint.
        var decoded = _transcoder.ToJson(DescriptorSet, "transcoding.v1.Scalars", new byte[] { 14 << 3, 0x01 });

        Assert.Contains("stringValue: field 14 came as a varint, but the .proto declares it String", decoded.Error);
    }

    [Fact]
    public void ToJson_AStringThatIsntUtf8_IsAnError()
    {
        var decoded = _transcoder.ToJson(DescriptorSet, "transcoding.v1.Scalars", new byte[] { 14 << 3 | 2, 0x02, 0xC3, 0x28 });

        Assert.Contains("stringValue: isn't valid UTF-8", decoded.Error);
    }

    [Fact]
    public void ToJson_TheLastOneofMemberWins_AndALaterScalarOverridesAnEarlier()
    {
        var bytes = new Presence { ChoiceString = "first" }.ToByteArray()
            .Concat(new Presence { ChoiceInt64 = 9 }.ToByteArray())
            .Concat(new Presence { Renamed = "a" }.ToByteArray())
            .Concat(new Presence { Renamed = "b" }.ToByteArray())
            .ToArray();

        var decoded = _transcoder.ToJson(DescriptorSet, "transcoding.v1.Presence", bytes);

        Assert.Equal(new Presence { ChoiceInt64 = 9, Renamed = "b" }, Parser.Parse<Presence>(decoded.Json!));
    }

    [Fact]
    public void FindMethod_GivesTheRequestAndResponseTypes()
    {
        var method = _transcoder.FindMethod(DescriptorSet, "/transcoding.v1.Fixture/Echo");

        Assert.Equal(("transcoding.v1.Scalars", "transcoding.v1.Scalars"), (method?.RequestType, method?.ResponseType));
        Assert.Null(_transcoder.FindMethod(DescriptorSet, "/transcoding.v1.Fixture/Nope"));
    }

    /// <summary>
    /// What the mock will do (plan step G4): each sample operation's generated example, its
    /// placeholders filled in, becomes the response message, and reads back as JSON its schema accepts.
    /// </summary>
    [Fact]
    public void SampleExamples_EncodeAndReadBackAsJsonTheirSchemaAccepts()
    {
        var validator = new Infrastructure.SchemaValidation.SchemaValidator();
        foreach (var spec in new ProtoSpecificationParser().Parse(Specifications.ProtoSpecificationParserTests.Samples()).Specifications)
        {
            foreach (var operation in spec.Operations)
            {
                var method = _transcoder.FindMethod(spec.DescriptorSet, operation.OperationKey["RPC ".Length..]);
                Assert.NotNull(method);
                var example = operation.ExampleJson!.Replace("{{now}}", "2026-10-11T09:30:00Z").Replace("{{uuid}}", Guid.NewGuid().ToString());

                var encoded = _transcoder.ToBinary(spec.DescriptorSet, method.ResponseType, example);
                Assert.True(encoded.Error is null, $"{operation.OperationKey}: {encoded.Error}");
                var decoded = _transcoder.ToJson(spec.DescriptorSet, method.ResponseType, encoded.Bytes);
                Assert.True(decoded.Error is null, $"{operation.OperationKey}: {decoded.Error}");

                var validation = validator.Validate(operation.ResponseSchemaJson!, decoded.Json!);
                Assert.True(validation.IsValid, $"{operation.OperationKey}: {string.Join("; ", validation.Errors)}\n{decoded.Json}");
            }
        }
    }

    [Fact]
    public void DescriptorsThatDontLoad_AreAnErrorOrNoMethod()
    {
        byte[] broken = [0x0A, 0x05, 0x01];

        Assert.Contains("can't be loaded", _transcoder.ToJson(broken, "x.Y", Array.Empty<byte>()).Error);
        Assert.Contains("can't be loaded", _transcoder.ToBinary(broken, "x.Y", "{}").Error);
        Assert.Null(_transcoder.FindMethod(broken, "/x.S/M"));
    }

    private static Dictionary<string, string> Fixtures()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Fixtures", "proto");
        return Directory.EnumerateFiles(root, "*.proto").ToDictionary(file => Path.GetRelativePath(root, file), File.ReadAllText);
    }
}
