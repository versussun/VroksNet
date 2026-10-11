using System.Text.Json;
using System.Text.Json.Nodes;
using Google.Protobuf.Reflection;

namespace VroksNet.Infrastructure.Specifications.Proto;

/// <summary>
/// A self-contained JSON Schema (2020-12) for a protobuf message in its proto3 JSON form (ADR 0004),
/// so the existing JSON pipeline — <see cref="SchemaExampleGenerator"/>, the schema validator, the
/// call history — works on gRPC messages as on any other.
/// <list type="bullet">
/// <item>Each message is a <c>$defs</c> entry named by its full name; fields use their JSON names.
/// Proto3 fields are all optional, so only proto2 <c>required</c> fields are required.</item>
/// <item>64-bit integers accept a number or a decimal string, as the JSON mapping does (the
/// canonical form is the string); enums their name or number; floats also "NaN"/"Infinity"/"-Infinity".</item>
/// <item>A <c>oneof</c> is an <c>allOf</c> entry allowing at most one of its fields, which are
/// declared only there, so a generated example sets just its first field. Unknown properties are
/// refused through <c>unevaluatedProperties</c>.</item>
/// <item>The well-known types take their special JSON forms: Timestamp a date-time string, Duration
/// "1.5s", wrappers their wrapped value, Struct/Value/ListValue any JSON, Any an object with "@type".</item>
/// </list>
/// </summary>
public static class ProtoJsonSchema
{
    private const string Dialect = "https://json-schema.org/draft/2020-12/schema";
    private static readonly JsonSerializerOptions SchemaJsonOptions = new() { WriteIndented = true };

    public static string For(MessageDescriptor message)
    {
        var defs = new JsonObject();
        var root = MessageReference(message, defs);
        var schema = new JsonObject { ["$schema"] = Dialect };
        foreach (var (name, value) in root)
        {
            schema[name] = value?.DeepClone();
        }

        if (defs.Count > 0)
        {
            schema["$defs"] = defs;
        }

        return schema.ToJsonString(SchemaJsonOptions);
    }

    /// <summary>A well-known type's own schema, or a <c>$ref</c> to the message's <c>$defs</c> entry (added on first use).</summary>
    private static JsonObject MessageReference(MessageDescriptor message, JsonObject defs)
    {
        if (WellKnown(message) is { } wellKnown)
        {
            return wellKnown;
        }

        if (!defs.ContainsKey(message.FullName))
        {
            // Reserve the name first, so a message that contains itself refers back instead of recursing.
            defs[message.FullName] = new JsonObject();
            defs[message.FullName] = MessageSchema(message, defs);
        }

        return new JsonObject { ["$ref"] = $"#/$defs/{message.FullName}" };
    }

    private static JsonObject MessageSchema(MessageDescriptor message, JsonObject defs)
    {
        var properties = new JsonObject();
        var required = new JsonArray();
        var oneofs = new JsonArray();
        foreach (var field in message.Fields.InDeclarationOrder())
        {
            if (field.RealContainingOneof is not null)
            {
                continue;
            }

            properties[field.JsonName] = FieldSchema(field, defs);
            if (field.IsRequired)
            {
                required.Add(field.JsonName);
            }
        }

        foreach (var oneof in message.Oneofs.Where(oneof => !oneof.IsSynthetic))
        {
            oneofs.Add(OneofSchema(oneof, defs));
        }

        var schema = new JsonObject { ["type"] = "object", ["properties"] = properties };
        if (required.Count > 0)
        {
            schema["required"] = required;
        }

        if (oneofs.Count > 0)
        {
            schema["allOf"] = oneofs;
        }

        schema["unevaluatedProperties"] = false;
        return schema;
    }

    /// <summary>One branch per field (that field and none of the others), then one for none of them set.</summary>
    private static JsonObject OneofSchema(OneofDescriptor oneof, JsonObject defs)
    {
        var names = oneof.Fields.Select(field => field.JsonName).ToList();
        var branches = new JsonArray();
        foreach (var field in oneof.Fields)
        {
            var branch = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject { [field.JsonName] = FieldSchema(field, defs) },
                ["required"] = new JsonArray(field.JsonName)
            };
            var others = names.Where(name => name != field.JsonName).ToList();
            if (others.Count > 0)
            {
                branch["not"] = AnyRequired(others);
            }

            branches.Add(branch);
        }

        branches.Add(new JsonObject { ["not"] = AnyRequired(names) });
        return new JsonObject { ["oneOf"] = branches };
    }

    private static JsonObject AnyRequired(IEnumerable<string> names)
        => new() { ["anyOf"] = new JsonArray([.. names.Select(name => (JsonNode)new JsonObject { ["required"] = new JsonArray(name) })]) };

    private static JsonObject FieldSchema(FieldDescriptor field, JsonObject defs)
    {
        if (field.IsMap)
        {
            var entry = field.MessageType;
            var key = entry.FindFieldByNumber(1);
            var value = entry.FindFieldByNumber(2);
            var map = new JsonObject { ["type"] = "object", ["additionalProperties"] = SingleSchema(value, defs) };
            if (MapKeyPattern(key.FieldType) is { } pattern)
            {
                map["propertyNames"] = new JsonObject { ["pattern"] = pattern };
            }

            return map;
        }

        return field.IsRepeated
            ? new JsonObject { ["type"] = "array", ["items"] = SingleSchema(field, defs) }
            : SingleSchema(field, defs);
    }

    private static JsonObject SingleSchema(FieldDescriptor field, JsonObject defs) => field.FieldType switch
    {
        FieldType.Double or FieldType.Float => FloatingPoint(),
        FieldType.Int32 or FieldType.SInt32 or FieldType.SFixed32 => Integer(int.MinValue, int.MaxValue),
        FieldType.UInt32 or FieldType.Fixed32 => Integer(0, uint.MaxValue),
        FieldType.Int64 or FieldType.SInt64 or FieldType.SFixed64 => Int64(signed: true),
        FieldType.UInt64 or FieldType.Fixed64 => Int64(signed: false),
        FieldType.Bool => new JsonObject { ["type"] = "boolean" },
        FieldType.String => new JsonObject { ["type"] = "string" },
        FieldType.Bytes => Bytes(),
        FieldType.Enum => EnumSchema(field.EnumType),
        FieldType.Message or FieldType.Group => MessageReference(field.MessageType, defs),
        _ => new JsonObject()
    };

    private static JsonObject EnumSchema(EnumDescriptor enumType)
    {
        if (enumType.FullName == "google.protobuf.NullValue")
        {
            return new JsonObject { ["type"] = "null" };
        }

        var names = new JsonArray([.. enumType.Values.Select(value => (JsonNode)JsonValue.Create(value.Name))]);
        // By name (canonical), or by number — also how an unknown value comes out of the wire.
        return new JsonObject { ["anyOf"] = new JsonArray(new JsonObject { ["enum"] = names }, Integer(int.MinValue, int.MaxValue)) };
    }

    private static JsonObject? WellKnown(MessageDescriptor message) => message.FullName switch
    {
        "google.protobuf.Timestamp" => new JsonObject { ["type"] = "string", ["format"] = "date-time" },
        "google.protobuf.Duration" => new JsonObject { ["type"] = "string", ["pattern"] = @"^-?[0-9]+(\.[0-9]{1,9})?s$", ["examples"] = new JsonArray("1.5s") },
        "google.protobuf.FieldMask" => new JsonObject { ["type"] = "string" },
        "google.protobuf.Empty" => new JsonObject { ["type"] = "object", ["additionalProperties"] = false },
        "google.protobuf.Struct" => new JsonObject { ["type"] = "object" },
        "google.protobuf.ListValue" => new JsonObject { ["type"] = "array" },
        "google.protobuf.Value" => new JsonObject(),
        "google.protobuf.Any" => new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject { ["@type"] = new JsonObject { ["type"] = "string", ["examples"] = new JsonArray("type.googleapis.com/google.protobuf.Empty") } },
            ["required"] = new JsonArray("@type")
        },
        "google.protobuf.DoubleValue" or "google.protobuf.FloatValue" => FloatingPoint(),
        "google.protobuf.Int64Value" => Int64(signed: true),
        "google.protobuf.UInt64Value" => Int64(signed: false),
        "google.protobuf.Int32Value" => Integer(int.MinValue, int.MaxValue),
        "google.protobuf.UInt32Value" => Integer(0, uint.MaxValue),
        "google.protobuf.BoolValue" => new JsonObject { ["type"] = "boolean" },
        "google.protobuf.StringValue" => new JsonObject { ["type"] = "string" },
        "google.protobuf.BytesValue" => Bytes(),
        _ => null
    };

    private static JsonObject Integer(long minimum, long maximum)
        => new() { ["type"] = "integer", ["minimum"] = minimum, ["maximum"] = maximum };

    private static JsonObject Int64(bool signed) => new()
    {
        ["anyOf"] = new JsonArray(
            signed ? Integer(long.MinValue, long.MaxValue) : new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = ulong.MaxValue },
            new JsonObject { ["type"] = "string", ["pattern"] = signed ? "^-?[0-9]+$" : "^[0-9]+$" })
    };

    private static JsonObject FloatingPoint() => new()
    {
        ["anyOf"] = new JsonArray(new JsonObject { ["type"] = "number" }, new JsonObject { ["enum"] = new JsonArray("NaN", "Infinity", "-Infinity") })
    };

    /// <summary>Base64, standard or URL-safe, padded or not — the JSON mapping accepts each.</summary>
    private static JsonObject Bytes()
        => new() { ["type"] = "string", ["format"] = "byte", ["pattern"] = "^[A-Za-z0-9+/_-]*={0,2}$" };

    /// <summary>JSON object keys are strings; a map with integer or bool keys writes them as such.</summary>
    private static string? MapKeyPattern(FieldType keyType) => keyType switch
    {
        FieldType.Bool => "^(true|false)$",
        FieldType.UInt32 or FieldType.Fixed32 or FieldType.UInt64 or FieldType.Fixed64 => "^[0-9]+$",
        FieldType.String => null,
        _ => "^-?[0-9]+$"
    };
}
