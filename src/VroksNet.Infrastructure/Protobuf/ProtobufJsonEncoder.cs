using System.Globalization;
using System.Text.Json;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace VroksNet.Infrastructure.Protobuf;

/// <summary>
/// Writes proto3 JSON as protobuf wire format, from descriptors alone. It accepts every form the
/// JSON mapping allows: JSON or original field names, 64-bit integers as numbers or strings, enums by
/// name or number, "NaN"/"Infinity" for floats, standard or URL-safe base64, <c>null</c> for "not set".
/// Fields are written in field-number order, and an implicit-presence field holding its default isn't
/// written at all — what the generated code does, so the bytes match it.
/// </summary>
internal sealed class ProtobufJsonEncoder(ProtobufDescriptors descriptors)
{
    private const int MaxDepth = 100;

    public byte[] Encode(MessageDescriptor message, JsonElement json) => Message(message, json, string.Empty, 0);

    private byte[] Message(MessageDescriptor message, JsonElement json, string path, int depth)
    {
        if (depth > MaxDepth)
        {
            throw ProtobufTranscodeException.At(path, $"nested more than {MaxDepth} levels deep.");
        }

        if (WellKnownJson.IsSpecial(message.FullName))
        {
            return Special(message, json, path, depth);
        }

        if (json.ValueKind != JsonValueKind.Object)
        {
            throw ProtobufTranscodeException.At(path, $"expected a JSON object for {message.Name}, got {Describe(json)}.");
        }

        var fields = new SortedDictionary<int, (FieldDescriptor Field, JsonElement Value, string Path)>();
        var oneofs = new Dictionary<OneofDescriptor, string>();
        foreach (var property in json.EnumerateObject())
        {
            var fieldPath = Child(path, property.Name);
            var field = FindField(message, property.Name)
                ?? throw ProtobufTranscodeException.At(fieldPath, $"{message.Name} has no field \"{property.Name}\".");
            if (fields.TryGetValue(field.FieldNumber, out var earlier))
            {
                throw ProtobufTranscodeException.At(fieldPath, $"field {field.Name} is given twice (also as \"{earlier.Path}\").");
            }

            // null means "not set" — except for google.protobuf.Value, where it's the JSON null.
            if (property.Value.ValueKind == JsonValueKind.Null && !(field.FieldType == FieldType.Message && field.MessageType.FullName == WellKnownJson.Value && !field.IsRepeated))
            {
                continue;
            }

            if (field.RealContainingOneof is { } oneof)
            {
                if (oneofs.TryGetValue(oneof, out var other))
                {
                    throw ProtobufTranscodeException.At(fieldPath, $"only one field of oneof {oneof.Name} can be set, but \"{other}\" is set too.");
                }

                oneofs[oneof] = property.Name;
            }

            fields[field.FieldNumber] = (field, property.Value, fieldPath);
        }

        return Write(output =>
        {
            foreach (var (field, value, fieldPath) in fields.Values)
            {
                Field(output, field, value, fieldPath, depth);
            }
        });
    }

    private void Field(CodedOutputStream output, FieldDescriptor field, JsonElement json, string path, int depth)
    {
        if (field.IsMap)
        {
            Map(output, field, json, path, depth);
        }
        else if (field.IsRepeated)
        {
            if (json.ValueKind != JsonValueKind.Array)
            {
                throw ProtobufTranscodeException.At(path, $"expected a JSON array for repeated field {field.Name}, got {Describe(json)}.");
            }

            var items = json.EnumerateArray().Select((item, index) => (item, $"{path}[{index}]")).ToList();
            if (items.Count == 0)
            {
                return;
            }

            // FieldDescriptor.IsPacked is true for every repeated proto3 field, strings and messages
            // included; only numeric, bool and enum fields can actually be packed.
            if (field.IsPacked && field.FieldType is not (FieldType.String or FieldType.Bytes or FieldType.Message or FieldType.Group))
            {
                var packed = Write(inner => items.ForEach(item => Value(inner, field, item.item, item.Item2, depth, withTag: false)));
                output.WriteTag(field.FieldNumber, WireFormat.WireType.LengthDelimited);
                output.WriteBytes(ByteString.CopyFrom(packed));
            }
            else
            {
                items.ForEach(item => Value(output, field, item.item, item.Item2, depth, withTag: true));
            }
        }
        else
        {
            Value(output, field, json, path, depth, withTag: true, skipDefault: !field.HasPresence);
        }
    }

    /// <summary>Each entry is a message of key (1) and value (2), in the order the JSON gives them.</summary>
    /// <remarks>A missing key or value reads back as its default (see <see cref="ProtobufJsonDecoder"/>).</remarks>
    private void Map(CodedOutputStream output, FieldDescriptor field, JsonElement json, string path, int depth)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            throw ProtobufTranscodeException.At(path, $"expected a JSON object for map field {field.Name}, got {Describe(json)}.");
        }

        var keyField = field.MessageType.FindFieldByNumber(1);
        var valueField = field.MessageType.FindFieldByNumber(2);
        foreach (var entry in json.EnumerateObject())
        {
            var entryPath = Child(path, entry.Name);
            if (entry.Value.ValueKind == JsonValueKind.Null && !(valueField.FieldType == FieldType.Message && valueField.MessageType.FullName == WellKnownJson.Value))
            {
                throw ProtobufTranscodeException.At(entryPath, "a map value can't be null.");
            }

            // Like the generated code, a key or scalar value at its default isn't written; a message value always is.
            var bytes = Write(inner =>
            {
                MapKey(inner, keyField, entry.Name, entryPath);
                Value(inner, valueField, entry.Value, entryPath, depth, withTag: true, skipDefault: true);
            });
            output.WriteTag(field.FieldNumber, WireFormat.WireType.LengthDelimited);
            output.WriteBytes(ByteString.CopyFrom(bytes));
        }
    }

    /// <summary>JSON object keys are strings, so a map with bool or integer keys writes them as text.</summary>
    private static void MapKey(CodedOutputStream output, FieldDescriptor keyField, string key, string path)
    {
        using var document = keyField.FieldType switch
        {
            FieldType.String => JsonDocument.Parse(JsonSerializer.Serialize(key)),
            FieldType.Bool when key is "true" or "false" => JsonDocument.Parse(key),
            FieldType.Bool => throw ProtobufTranscodeException.At(path, $"a bool map key must be \"true\" or \"false\", not \"{key}\"."),
            _ => JsonDocument.Parse(JsonSerializer.Serialize(key)) // integers parse from strings anyway
        };
        Scalar(output, keyField, document.RootElement, path, withTag: true, skipDefault: true);
    }

    private void Value(CodedOutputStream output, FieldDescriptor field, JsonElement json, string path, int depth, bool withTag, bool skipDefault = false)
    {
        if (field.FieldType is FieldType.Message or FieldType.Group)
        {
            var bytes = Message(field.MessageType, json, path, depth + 1);
            output.WriteTag(field.FieldNumber, WireFormat.WireType.LengthDelimited);
            output.WriteBytes(ByteString.CopyFrom(bytes));
            return;
        }

        if (json.ValueKind == JsonValueKind.Null && !(field.FieldType == FieldType.Enum && field.EnumType.FullName == WellKnownJson.NullValue))
        {
            throw ProtobufTranscodeException.At(path, "null isn't allowed here.");
        }

        Scalar(output, field, json, path, withTag, skipDefault);
    }

    private static void Scalar(CodedOutputStream output, FieldDescriptor field, JsonElement json, string path, bool withTag, bool skipDefault)
    {
        switch (field.FieldType)
        {
            case FieldType.Double:
                var number = FloatingPoint(json, path, single: false);
                if (!(skipDefault && BitConverter.DoubleToInt64Bits(number) == 0))
                {
                    Tag(output, field, WireFormat.WireType.Fixed64, withTag);
                    output.WriteDouble(number);
                }

                break;
            case FieldType.Float:
                var single = (float)FloatingPoint(json, path, single: true);
                if (!(skipDefault && BitConverter.SingleToInt32Bits(single) == 0))
                {
                    Tag(output, field, WireFormat.WireType.Fixed32, withTag);
                    output.WriteFloat(single);
                }

                break;
            case FieldType.Int32 or FieldType.SInt32 or FieldType.SFixed32:
                var int32 = (int)Integer(json, path, int.MinValue, int.MaxValue);
                if (!(skipDefault && int32 == 0))
                {
                    WriteInt32(output, field, int32, withTag);
                }

                break;
            case FieldType.UInt32 or FieldType.Fixed32:
                var uint32 = (uint)Integer(json, path, 0, uint.MaxValue);
                if (!(skipDefault && uint32 == 0))
                {
                    Tag(output, field, field.FieldType == FieldType.Fixed32 ? WireFormat.WireType.Fixed32 : WireFormat.WireType.Varint, withTag);
                    if (field.FieldType == FieldType.Fixed32)
                    {
                        output.WriteFixed32(uint32);
                    }
                    else
                    {
                        output.WriteUInt32(uint32);
                    }
                }

                break;
            case FieldType.Int64 or FieldType.SInt64 or FieldType.SFixed64:
                var int64 = (long)Integer(json, path, long.MinValue, long.MaxValue);
                if (!(skipDefault && int64 == 0))
                {
                    WriteInt64(output, field, int64, withTag);
                }

                break;
            case FieldType.UInt64 or FieldType.Fixed64:
                var uint64 = (ulong)Integer(json, path, 0, ulong.MaxValue);
                if (!(skipDefault && uint64 == 0))
                {
                    Tag(output, field, field.FieldType == FieldType.Fixed64 ? WireFormat.WireType.Fixed64 : WireFormat.WireType.Varint, withTag);
                    if (field.FieldType == FieldType.Fixed64)
                    {
                        output.WriteFixed64(uint64);
                    }
                    else
                    {
                        output.WriteUInt64(uint64);
                    }
                }

                break;
            case FieldType.Bool:
                if (json.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    throw ProtobufTranscodeException.At(path, $"expected true or false, got {Describe(json)}.");
                }

                if (!(skipDefault && !json.GetBoolean()))
                {
                    Tag(output, field, WireFormat.WireType.Varint, withTag);
                    output.WriteBool(json.GetBoolean());
                }

                break;
            case FieldType.String:
                if (json.ValueKind != JsonValueKind.String)
                {
                    throw ProtobufTranscodeException.At(path, $"expected a string, got {Describe(json)}.");
                }

                var text = json.GetString()!; // a String element always has a value
                if (!(skipDefault && text.Length == 0))
                {
                    Tag(output, field, WireFormat.WireType.LengthDelimited, withTag);
                    output.WriteString(text);
                }

                break;
            case FieldType.Bytes:
                var bytes = Base64(json, path);
                if (!(skipDefault && bytes.Length == 0))
                {
                    Tag(output, field, WireFormat.WireType.LengthDelimited, withTag);
                    output.WriteBytes(ByteString.CopyFrom(bytes));
                }

                break;
            case FieldType.Enum:
                var value = EnumNumber(field.EnumType, json, path);
                if (!(skipDefault && value == 0))
                {
                    Tag(output, field, WireFormat.WireType.Varint, withTag);
                    output.WriteEnum(value);
                }

                break;
            default:
                throw ProtobufTranscodeException.At(path, $"field type {field.FieldType} isn't supported.");
        }
    }

    private static void WriteInt32(CodedOutputStream output, FieldDescriptor field, int value, bool withTag)
    {
        switch (field.FieldType)
        {
            case FieldType.SInt32:
                Tag(output, field, WireFormat.WireType.Varint, withTag);
                output.WriteSInt32(value);
                break;
            case FieldType.SFixed32:
                Tag(output, field, WireFormat.WireType.Fixed32, withTag);
                output.WriteSFixed32(value);
                break;
            default:
                Tag(output, field, WireFormat.WireType.Varint, withTag);
                output.WriteInt32(value);
                break;
        }
    }

    private static void WriteInt64(CodedOutputStream output, FieldDescriptor field, long value, bool withTag)
    {
        switch (field.FieldType)
        {
            case FieldType.SInt64:
                Tag(output, field, WireFormat.WireType.Varint, withTag);
                output.WriteSInt64(value);
                break;
            case FieldType.SFixed64:
                Tag(output, field, WireFormat.WireType.Fixed64, withTag);
                output.WriteSFixed64(value);
                break;
            default:
                Tag(output, field, WireFormat.WireType.Varint, withTag);
                output.WriteInt64(value);
                break;
        }
    }

    private static void Tag(CodedOutputStream output, FieldDescriptor field, WireFormat.WireType wireType, bool withTag)
    {
        if (withTag)
        {
            output.WriteTag(field.FieldNumber, wireType);
        }
    }

    /// <summary>The well-known types whose JSON isn't an object of their fields.</summary>
    private byte[] Special(MessageDescriptor message, JsonElement json, string path, int depth)
    {
        if (WellKnownJson.IsWrapper(message.FullName))
        {
            return Write(output => Value(output, message.FindFieldByNumber(1), json, path, depth, withTag: true, skipDefault: true));
        }

        switch (message.FullName)
        {
            case WellKnownJson.Timestamp or WellKnownJson.Duration:
                if (json.ValueKind != JsonValueKind.String)
                {
                    throw ProtobufTranscodeException.At(path, $"expected a string for {message.Name}, got {Describe(json)}.");
                }

                var (seconds, nanos) = message.FullName == WellKnownJson.Timestamp
                    ? WellKnownJson.ParseTimestamp(json.GetString()!, path)
                    : WellKnownJson.ParseDuration(json.GetString()!, path);
                return Write(output =>
                {
                    if (seconds != 0)
                    {
                        output.WriteTag(1, WireFormat.WireType.Varint);
                        output.WriteInt64(seconds);
                    }

                    if (nanos != 0)
                    {
                        output.WriteTag(2, WireFormat.WireType.Varint);
                        output.WriteInt32(nanos);
                    }
                });
            case WellKnownJson.FieldMask:
                if (json.ValueKind != JsonValueKind.String)
                {
                    throw ProtobufTranscodeException.At(path, $"expected a string of comma-separated paths for FieldMask, got {Describe(json)}.");
                }

                return Write(output =>
                {
                    foreach (var fieldPath in WellKnownJson.ParseFieldMask(json.GetString()!))
                    {
                        output.WriteTag(1, WireFormat.WireType.LengthDelimited);
                        output.WriteString(fieldPath);
                    }
                });
            case WellKnownJson.Struct:
                return Write(output => Field(output, message.FindFieldByNumber(1), json, path, depth));
            case WellKnownJson.ListValue:
                return Write(output => Field(output, message.FindFieldByNumber(1), json, path, depth));
            case WellKnownJson.Value:
                return Write(output => ValueKind(output, message, json, path, depth));
            case WellKnownJson.Any:
                return Any(json, path, depth);
            default:
                throw ProtobufTranscodeException.At(path, $"{message.FullName} isn't supported.");
        }
    }

    /// <summary>google.protobuf.Value: whichever of its oneof the JSON value is.</summary>
    private void ValueKind(CodedOutputStream output, MessageDescriptor value, JsonElement json, string path, int depth)
    {
        switch (json.ValueKind)
        {
            case JsonValueKind.Null:
                output.WriteTag(1, WireFormat.WireType.Varint);
                output.WriteEnum(0);
                break;
            case JsonValueKind.Number:
                output.WriteTag(2, WireFormat.WireType.Fixed64);
                output.WriteDouble(json.GetDouble());
                break;
            case JsonValueKind.String:
                output.WriteTag(3, WireFormat.WireType.LengthDelimited);
                output.WriteString(json.GetString()!);
                break;
            case JsonValueKind.True or JsonValueKind.False:
                output.WriteTag(4, WireFormat.WireType.Varint);
                output.WriteBool(json.GetBoolean());
                break;
            case JsonValueKind.Object:
                Value(output, value.FindFieldByNumber(5), json, path, depth, withTag: true);
                break;
            case JsonValueKind.Array:
                Value(output, value.FindFieldByNumber(6), json, path, depth, withTag: true);
                break;
        }
    }

    /// <summary>{"@type": "type.googleapis.com/x.Y", …the message's fields} — or "value" for a type with a special JSON form.</summary>
    private byte[] Any(JsonElement json, string path, int depth)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            throw ProtobufTranscodeException.At(path, $"expected a JSON object for Any, got {Describe(json)}.");
        }

        if (!json.TryGetProperty("@type", out var typeUrl) || typeUrl.ValueKind != JsonValueKind.String)
        {
            throw ProtobufTranscodeException.At(path, "an Any needs an \"@type\" string.");
        }

        var url = typeUrl.GetString()!;
        var type = descriptors.FindMessage(url[(url.LastIndexOf('/') + 1)..])
            ?? throw ProtobufTranscodeException.At(Child(path, "@type"), $"\"{url}\" names a type the .proto files don't declare.");

        byte[] packed;
        if (WellKnownJson.IsSpecial(type.FullName))
        {
            if (!json.TryGetProperty("value", out var special))
            {
                throw ProtobufTranscodeException.At(path, $"an Any holding {type.Name} keeps it in \"value\".");
            }

            packed = Message(type, special, Child(path, "value"), depth + 1);
        }
        else
        {
            var fields = json.EnumerateObject().Where(property => property.Name != "@type").ToDictionary(property => property.Name, property => property.Value);
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(fields));
            packed = Message(type, document.RootElement, path, depth + 1);
        }

        return Write(output =>
        {
            output.WriteTag(1, WireFormat.WireType.LengthDelimited);
            output.WriteString(url);
            if (packed.Length > 0)
            {
                output.WriteTag(2, WireFormat.WireType.LengthDelimited);
                output.WriteBytes(ByteString.CopyFrom(packed));
            }
        });
    }

    private static FieldDescriptor? FindField(MessageDescriptor message, string name)
        => message.Fields.InDeclarationOrder().FirstOrDefault(field => field.JsonName == name)
            ?? message.FindFieldByName(name);

    /// <summary>An integer in [min, max] from a JSON number or a decimal string, either possibly with an exponent ("1e3").</summary>
    private static decimal Integer(JsonElement json, string path, decimal min, decimal max)
    {
        decimal value;
        try
        {
            value = json.ValueKind switch
            {
                JsonValueKind.Number => json.GetDecimal(),
                JsonValueKind.String => decimal.Parse(json.GetString()!, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture),
                _ => throw ProtobufTranscodeException.At(path, $"expected an integer, got {Describe(json)}.")
            };
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            throw ProtobufTranscodeException.At(path, $"{json.GetRawText()} isn't an integer within {min}…{max}.");
        }

        if (value != decimal.Truncate(value) || value < min || value > max)
        {
            throw ProtobufTranscodeException.At(path, $"{json.GetRawText()} isn't an integer within {min}…{max}.");
        }

        return value;
    }

    private static double FloatingPoint(JsonElement json, string path, bool single)
    {
        double value;
        switch (json.ValueKind)
        {
            case JsonValueKind.Number:
                value = json.GetDouble();
                break;
            case JsonValueKind.String:
                var text = json.GetString()!;
                value = text switch
                {
                    "NaN" => double.NaN,
                    "Infinity" => double.PositiveInfinity,
                    "-Infinity" => double.NegativeInfinity,
                    _ => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && !double.IsInfinity(parsed) && !double.IsNaN(parsed)
                        ? parsed
                        : throw ProtobufTranscodeException.At(path, $"\"{text}\" isn't a number.")
                };
                break;
            default:
                throw ProtobufTranscodeException.At(path, $"expected a number, got {Describe(json)}.");
        }

        if (single && double.IsFinite(value) && Math.Abs(value) > float.MaxValue)
        {
            throw ProtobufTranscodeException.At(path, $"{json.GetRawText()} is too large for a float.");
        }

        return value;
    }

    /// <summary>Standard or URL-safe base64, padded or not.</summary>
    private static byte[] Base64(JsonElement json, string path)
    {
        if (json.ValueKind != JsonValueKind.String)
        {
            throw ProtobufTranscodeException.At(path, $"expected a base64 string, got {Describe(json)}.");
        }

        var text = json.GetString()!.Replace('-', '+').Replace('_', '/');
        try
        {
            return Convert.FromBase64String(text.PadRight(text.Length + (4 - text.Length % 4) % 4, '='));
        }
        catch (FormatException)
        {
            throw ProtobufTranscodeException.At(path, "isn't valid base64.");
        }
    }

    private static int EnumNumber(EnumDescriptor enumType, JsonElement json, string path)
    {
        switch (json.ValueKind)
        {
            case JsonValueKind.Null:
                return 0; // google.protobuf.NullValue
            case JsonValueKind.String:
                return enumType.FindValueByName(json.GetString()!)?.Number
                    ?? throw ProtobufTranscodeException.At(path, $"\"{json.GetString()}\" isn't a value of {enumType.Name} ({string.Join(", ", enumType.Values.Select(value => value.Name))}).");
            case JsonValueKind.Number when json.TryGetInt32(out var number):
                return number;
            default:
                throw ProtobufTranscodeException.At(path, $"expected a {enumType.Name} name or number, got {Describe(json)}.");
        }
    }

    private static byte[] Write(Action<CodedOutputStream> write)
    {
        using var buffer = new MemoryStream();
        var output = new CodedOutputStream(buffer);
        write(output);
        output.Flush();
        return buffer.ToArray();
    }

    private static string Child(string path, string name) => path.Length == 0 ? name : $"{path}.{name}";

    private static string Describe(JsonElement json) => json.ValueKind switch
    {
        JsonValueKind.Object => "an object",
        JsonValueKind.Array => "an array",
        JsonValueKind.String => $"the string {json.GetRawText()}",
        JsonValueKind.Number => $"the number {json.GetRawText()}",
        JsonValueKind.True or JsonValueKind.False => json.GetRawText(),
        _ => "null"
    };
}
