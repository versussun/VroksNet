using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace VroksNet.Infrastructure.Protobuf;

/// <summary>
/// Reads protobuf wire format as canonical proto3 JSON, from descriptors alone, with the semantics of
/// a generated parser: the last value of a singular field wins (a message field merges), the last
/// member of a oneof wins, a repeated scalar may come packed or not, an implicit-presence field holding
/// its default is left out. Unlike a generated parser, it is strict where a contract test needs it: a
/// known field with the wrong wire type and a string that isn't UTF-8 are errors. Fields the .proto
/// doesn't declare are skipped and reported in <see cref="Warnings"/>.
/// </summary>
internal sealed class ProtobufJsonDecoder(ProtobufDescriptors descriptors)
{
    private const int MaxDepth = 100;
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public List<string> Warnings { get; } = [];

    public void Decode(Utf8JsonWriter writer, MessageDescriptor message, ReadOnlyMemory<byte> bytes)
        => Message(writer, message, bytes, string.Empty, 0);

    /// <summary>One field occurrence on the wire, in the order read.</summary>
    private readonly record struct Raw(int Index, WireFormat.WireType WireType, ulong Number, ReadOnlyMemory<byte> Bytes);

    private void Message(Utf8JsonWriter writer, MessageDescriptor message, ReadOnlyMemory<byte> bytes, string path, int depth)
    {
        if (depth > MaxDepth)
        {
            throw ProtobufTranscodeException.At(path, $"nested more than {MaxDepth} levels deep.");
        }

        var fields = Read(message, bytes, path);
        if (WellKnownJson.IsSpecial(message.FullName))
        {
            Special(writer, message, fields, path, depth);
            return;
        }

        writer.WriteStartObject();
        foreach (var field in message.Fields.InFieldNumberOrder())
        {
            if (fields.TryGetValue(field.FieldNumber, out var occurrences))
            {
                Field(writer, field, occurrences, Child(path, field.JsonName), depth, writeName: true);
            }
        }

        writer.WriteEndObject();
    }

    /// <summary>The message's known fields by number, each with its occurrences; a oneof keeps only its last member.</summary>
    private Dictionary<int, List<Raw>> Read(MessageDescriptor message, ReadOnlyMemory<byte> bytes, string path)
    {
        var fields = new Dictionary<int, List<Raw>>();
        var input = new CodedInputStream(bytes.ToArray());
        var index = 0;
        try
        {
            uint tag;
            while ((tag = input.ReadTag()) != 0)
            {
                var number = WireFormat.GetTagFieldNumber(tag);
                var wireType = WireFormat.GetTagWireType(tag);
                var field = message.FindFieldByNumber(number);
                if (field is null)
                {
                    Warnings.Add($"{(path.Length == 0 ? message.Name : path)}: field {number} isn't in the .proto, so it was skipped.");
                    input.SkipLastField();
                    continue;
                }

                if (!Accepts(field, wireType))
                {
                    throw ProtobufTranscodeException.At(Child(path, field.JsonName), $"field {number} came as {Describe(wireType)}, but the .proto declares it {field.FieldType}.");
                }

                Raw raw = wireType switch
                {
                    WireFormat.WireType.Varint => new(index, wireType, input.ReadUInt64(), default),
                    WireFormat.WireType.Fixed64 => new(index, wireType, input.ReadFixed64(), default),
                    WireFormat.WireType.Fixed32 => new(index, wireType, input.ReadFixed32(), default),
                    _ => new(index, wireType, 0, input.ReadBytes().Memory)
                };
                index++;
                (fields.TryGetValue(number, out var list) ? list : fields[number] = []).Add(raw);
            }
        }
        catch (InvalidProtocolBufferException ex)
        {
            throw ProtobufTranscodeException.At(path, $"the bytes aren't a valid {message.Name} ({ex.Message.TrimEnd('.')}).");
        }

        foreach (var oneof in message.Oneofs.Where(oneof => !oneof.IsSynthetic))
        {
            var set = oneof.Fields.Where(member => fields.ContainsKey(member.FieldNumber)).ToList();
            if (set.Count > 1)
            {
                var last = set.MaxBy(member => fields[member.FieldNumber].Max(raw => raw.Index))!; // set isn't empty
                foreach (var member in set.Where(member => member != last))
                {
                    fields.Remove(member.FieldNumber);
                }
            }
        }

        return fields;
    }

    private void Field(Utf8JsonWriter writer, FieldDescriptor field, List<Raw> occurrences, string path, int depth, bool writeName)
    {
        if (field.IsMap)
        {
            Map(writer, field, occurrences, path, depth);
            return;
        }

        if (field.IsRepeated)
        {
            var items = occurrences.SelectMany(raw => Unpack(field, raw, path)).ToList();
            if (items.Count == 0)
            {
                return;
            }

            writer.WritePropertyName(field.JsonName);
            writer.WriteStartArray();
            for (var i = 0; i < items.Count; i++)
            {
                Single(writer, field, items[i], $"{path}[{i}]", depth);
            }

            writer.WriteEndArray();
            return;
        }

        // A message field's occurrences merge, as parsing their concatenation does; a scalar's last one wins.
        var value = field.FieldType is FieldType.Message
            ? new Raw(0, WireFormat.WireType.LengthDelimited, 0, Concatenate(occurrences))
            : occurrences[^1];
        if (!field.HasPresence && IsDefault(field, value))
        {
            return;
        }

        if (writeName)
        {
            writer.WritePropertyName(field.JsonName);
        }

        Single(writer, field, value, path, depth);
    }

    /// <summary>Each occurrence is an entry message (key 1, value 2); a later key replaces an earlier one's value, in the earlier one's place.</summary>
    private void Map(Utf8JsonWriter writer, FieldDescriptor field, List<Raw> occurrences, string path, int depth)
    {
        var keyField = field.MessageType.FindFieldByNumber(1);
        var valueField = field.MessageType.FindFieldByNumber(2);
        var entries = new List<(string Key, Raw? Value)>();
        var positions = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var occurrence in occurrences)
        {
            var entry = Read(field.MessageType, occurrence.Bytes, path);
            var key = entry.TryGetValue(1, out var keys) ? KeyText(keyField, keys[^1], path) : KeyText(keyField, default, path);
            Raw? value = entry.TryGetValue(2, out var values)
                ? valueField.FieldType is FieldType.Message ? new Raw(0, WireFormat.WireType.LengthDelimited, 0, Concatenate(values)) : values[^1]
                : null;
            if (positions.TryGetValue(key, out var position))
            {
                entries[position] = (key, value);
            }
            else
            {
                positions[key] = entries.Count;
                entries.Add((key, value));
            }
        }

        writer.WritePropertyName(field.JsonName);
        writer.WriteStartObject();
        foreach (var (key, value) in entries)
        {
            writer.WritePropertyName(key);
            Single(writer, valueField, value ?? DefaultOf(valueField), Child(path, key), depth);
        }

        writer.WriteEndObject();
    }

    private void Single(Utf8JsonWriter writer, FieldDescriptor field, Raw raw, string path, int depth)
    {
        switch (field.FieldType)
        {
            case FieldType.Double:
                FloatingPoint(writer, BitConverter.Int64BitsToDouble((long)raw.Number), single: false);
                break;
            case FieldType.Float:
                FloatingPoint(writer, BitConverter.Int32BitsToSingle((int)(uint)raw.Number), single: true);
                break;
            case FieldType.Int32 or FieldType.SFixed32:
                writer.WriteNumberValue((int)raw.Number);
                break;
            case FieldType.SInt32:
                writer.WriteNumberValue(ZigZag32((uint)raw.Number));
                break;
            case FieldType.UInt32 or FieldType.Fixed32:
                writer.WriteNumberValue((uint)raw.Number);
                break;
            case FieldType.Int64 or FieldType.SFixed64:
                writer.WriteStringValue(((long)raw.Number).ToString(System.Globalization.CultureInfo.InvariantCulture));
                break;
            case FieldType.SInt64:
                writer.WriteStringValue(ZigZag64(raw.Number).ToString(System.Globalization.CultureInfo.InvariantCulture));
                break;
            case FieldType.UInt64 or FieldType.Fixed64:
                writer.WriteStringValue(raw.Number.ToString(System.Globalization.CultureInfo.InvariantCulture));
                break;
            case FieldType.Bool:
                writer.WriteBooleanValue(raw.Number != 0);
                break;
            case FieldType.String:
                writer.WriteStringValue(Text(raw.Bytes, path));
                break;
            case FieldType.Bytes:
                writer.WriteBase64StringValue(raw.Bytes.Span);
                break;
            case FieldType.Enum:
                var number = (int)raw.Number;
                if (field.EnumType.FullName == WellKnownJson.NullValue)
                {
                    writer.WriteNullValue();
                }
                else if (field.EnumType.FindValueByNumber(number) is { } value)
                {
                    writer.WriteStringValue(value.Name);
                }
                else
                {
                    writer.WriteNumberValue(number);
                }

                break;
            case FieldType.Message:
                Message(writer, field.MessageType, raw.Bytes, path, depth + 1);
                break;
            default:
                throw ProtobufTranscodeException.At(path, $"field type {field.FieldType} isn't supported.");
        }
    }

    /// <summary>The well-known types whose JSON isn't an object of their fields.</summary>
    private void Special(Utf8JsonWriter writer, MessageDescriptor message, Dictionary<int, List<Raw>> fields, string path, int depth)
    {
        if (WellKnownJson.IsWrapper(message.FullName))
        {
            var valueField = message.FindFieldByNumber(1);
            Single(writer, valueField, fields.TryGetValue(1, out var values) ? values[^1] : DefaultOf(valueField), path, depth);
            return;
        }

        switch (message.FullName)
        {
            case WellKnownJson.Timestamp or WellKnownJson.Duration:
                var seconds = fields.TryGetValue(1, out var secondsRaw) ? (long)secondsRaw[^1].Number : 0;
                var nanos = fields.TryGetValue(2, out var nanosRaw) ? (int)nanosRaw[^1].Number : 0;
                writer.WriteStringValue(message.FullName == WellKnownJson.Timestamp
                    ? WellKnownJson.FormatTimestamp(seconds, nanos, path)
                    : WellKnownJson.FormatDuration(seconds, nanos, path));
                break;
            case WellKnownJson.FieldMask:
                writer.WriteStringValue(WellKnownJson.FormatFieldMask(fields.TryGetValue(1, out var paths) ? paths.Select(raw => Text(raw.Bytes, path)) : []));
                break;
            case WellKnownJson.Struct:
                if (fields.TryGetValue(1, out var entries))
                {
                    WriteMapBody(writer, message.FindFieldByNumber(1), entries, path, depth);
                }
                else
                {
                    writer.WriteStartObject();
                    writer.WriteEndObject();
                }

                break;
            case WellKnownJson.ListValue:
                writer.WriteStartArray();
                if (fields.TryGetValue(1, out var items))
                {
                    var valueType = message.FindFieldByNumber(1);
                    for (var i = 0; i < items.Count; i++)
                    {
                        Single(writer, valueType, items[i], $"{path}[{i}]", depth);
                    }
                }

                writer.WriteEndArray();
                break;
            case WellKnownJson.Value:
                var kind = message.Fields.InFieldNumberOrder().FirstOrDefault(field => fields.ContainsKey(field.FieldNumber))
                    ?? throw ProtobufTranscodeException.At(path, "a google.protobuf.Value with no kind set has no JSON form.");
                if (kind.FieldNumber == 2 && !double.IsFinite(BitConverter.Int64BitsToDouble((long)fields[2][^1].Number)))
                {
                    throw ProtobufTranscodeException.At(path, "a google.protobuf.Value can't hold NaN or Infinity in JSON.");
                }

                Single(writer, kind, kind.FieldType == FieldType.Message ? new Raw(0, WireFormat.WireType.LengthDelimited, 0, Concatenate(fields[kind.FieldNumber])) : fields[kind.FieldNumber][^1], path, depth);
                break;
            case WellKnownJson.Any:
                Any(writer, fields, path, depth);
                break;
        }
    }

    /// <summary>A Struct's fields as a JSON object (Struct has no field name of its own in JSON).</summary>
    private void WriteMapBody(Utf8JsonWriter writer, FieldDescriptor mapField, List<Raw> occurrences, string path, int depth)
    {
        using var buffer = new MemoryStream();
        using (var inner = new Utf8JsonWriter(buffer))
        {
            inner.WriteStartObject();
            Map(inner, mapField, occurrences, path, depth);
            inner.WriteEndObject();
        }

        using var document = JsonDocument.Parse(buffer.ToArray());
        document.RootElement.GetProperty(mapField.JsonName).WriteTo(writer);
    }

    private void Any(Utf8JsonWriter writer, Dictionary<int, List<Raw>> fields, string path, int depth)
    {
        var url = fields.TryGetValue(1, out var urls) ? Text(urls[^1].Bytes, path) : string.Empty;
        var packed = fields.TryGetValue(2, out var values) ? values[^1].Bytes : ReadOnlyMemory<byte>.Empty;
        var type = descriptors.FindMessage(url[(url.LastIndexOf('/') + 1)..]);
        writer.WriteStartObject();
        writer.WriteString("@type", url);
        if (type is null)
        {
            Warnings.Add($"{(path.Length == 0 ? "Any" : path)}: \"{url}\" names a type the .proto files don't declare, so its content was left out.");
        }
        else if (WellKnownJson.IsSpecial(type.FullName))
        {
            writer.WritePropertyName("value");
            Message(writer, type, packed, Child(path, "value"), depth + 1);
        }
        else
        {
            using var buffer = new MemoryStream();
            using (var inner = new Utf8JsonWriter(buffer))
            {
                Message(inner, type, packed, path, depth + 1);
            }

            using var document = JsonDocument.Parse(buffer.ToArray());
            foreach (var property in document.RootElement.EnumerateObject())
            {
                property.WriteTo(writer);
            }
        }

        writer.WriteEndObject();
    }

    /// <summary>A packed occurrence of a repeated scalar holds several values; anything else is one.</summary>
    private static IEnumerable<Raw> Unpack(FieldDescriptor field, Raw raw, string path)
    {
        if (raw.WireType != WireFormat.WireType.LengthDelimited || !IsPackable(field))
        {
            return [raw];
        }

        var values = new List<Raw>();
        var input = new CodedInputStream(raw.Bytes.ToArray());
        try
        {
            while (!input.IsAtEnd)
            {
                values.Add(WireTypeOf(field) switch
                {
                    WireFormat.WireType.Fixed64 => new Raw(raw.Index, WireFormat.WireType.Fixed64, input.ReadFixed64(), default),
                    WireFormat.WireType.Fixed32 => new Raw(raw.Index, WireFormat.WireType.Fixed32, input.ReadFixed32(), default),
                    _ => new Raw(raw.Index, WireFormat.WireType.Varint, input.ReadUInt64(), default)
                });
            }
        }
        catch (InvalidProtocolBufferException ex)
        {
            throw ProtobufTranscodeException.At(path, $"the packed values of {field.Name} are cut short ({ex.Message.TrimEnd('.')}).");
        }

        return values;
    }

    private static bool Accepts(FieldDescriptor field, WireFormat.WireType wireType)
        => wireType == WireTypeOf(field) || (field.IsRepeated && IsPackable(field) && wireType == WireFormat.WireType.LengthDelimited);

    private static bool IsPackable(FieldDescriptor field)
        => field.FieldType is not (FieldType.String or FieldType.Bytes or FieldType.Message or FieldType.Group) && !field.IsMap;

    private static WireFormat.WireType WireTypeOf(FieldDescriptor field) => field.FieldType switch
    {
        FieldType.Double or FieldType.Fixed64 or FieldType.SFixed64 => WireFormat.WireType.Fixed64,
        FieldType.Float or FieldType.Fixed32 or FieldType.SFixed32 => WireFormat.WireType.Fixed32,
        FieldType.String or FieldType.Bytes or FieldType.Message => WireFormat.WireType.LengthDelimited,
        FieldType.Group => WireFormat.WireType.StartGroup,
        _ => WireFormat.WireType.Varint
    };

    private static bool IsDefault(FieldDescriptor field, Raw raw) => field.FieldType switch
    {
        FieldType.String or FieldType.Bytes => raw.Bytes.Length == 0,
        FieldType.Message => false,
        _ => raw.Number == 0
    };

    /// <summary>A missing map value or wrapper value: zero, empty, or an empty message.</summary>
    private static Raw DefaultOf(FieldDescriptor field) => new(0, WireTypeOf(field), 0, ReadOnlyMemory<byte>.Empty);

    private static string KeyText(FieldDescriptor keyField, Raw raw, string path) => keyField.FieldType switch
    {
        FieldType.String => Text(raw.Bytes, path),
        FieldType.Bool => raw.Number != 0 ? "true" : "false",
        FieldType.SInt32 => ZigZag32((uint)raw.Number).ToString(System.Globalization.CultureInfo.InvariantCulture),
        FieldType.SInt64 => ZigZag64(raw.Number).ToString(System.Globalization.CultureInfo.InvariantCulture),
        FieldType.Int32 or FieldType.SFixed32 => ((int)raw.Number).ToString(System.Globalization.CultureInfo.InvariantCulture),
        FieldType.Int64 or FieldType.SFixed64 => ((long)raw.Number).ToString(System.Globalization.CultureInfo.InvariantCulture),
        FieldType.UInt32 or FieldType.Fixed32 => ((uint)raw.Number).ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => raw.Number.ToString(System.Globalization.CultureInfo.InvariantCulture)
    };

    private static void FloatingPoint(Utf8JsonWriter writer, double value, bool single)
    {
        if (double.IsNaN(value))
        {
            writer.WriteStringValue("NaN");
        }
        else if (double.IsPositiveInfinity(value))
        {
            writer.WriteStringValue("Infinity");
        }
        else if (double.IsNegativeInfinity(value))
        {
            writer.WriteStringValue("-Infinity");
        }
        else if (single)
        {
            writer.WriteNumberValue((float)value);
        }
        else
        {
            writer.WriteNumberValue(value);
        }
    }

    private static string Text(ReadOnlyMemory<byte> bytes, string path)
    {
        try
        {
            return StrictUtf8.GetString(bytes.Span);
        }
        catch (DecoderFallbackException)
        {
            throw ProtobufTranscodeException.At(path, "isn't valid UTF-8, which a protobuf string must be.");
        }
    }

    private static ReadOnlyMemory<byte> Concatenate(List<Raw> occurrences)
        => occurrences.Count == 1 ? occurrences[0].Bytes : occurrences.SelectMany(raw => raw.Bytes.ToArray()).ToArray();

    private static string Describe(WireFormat.WireType wireType) => wireType switch
    {
        WireFormat.WireType.Varint => "a varint",
        WireFormat.WireType.Fixed64 => "a 64-bit value",
        WireFormat.WireType.Fixed32 => "a 32-bit value",
        WireFormat.WireType.LengthDelimited => "length-delimited bytes",
        _ => "a group"
    };

    private static string Child(string path, string name) => path.Length == 0 ? name : $"{path}.{name}";

    /// <summary>sint32/sint64 store n as 2n (n ≥ 0) or −2n−1 (n &lt; 0), so small negatives stay short.</summary>
    private static int ZigZag32(uint value) => (int)(value >> 1) ^ -(int)(value & 1);

    private static long ZigZag64(ulong value) => (long)(value >> 1) ^ -(long)(value & 1);
}
