namespace VroksNet.Application.Abstractions;

/// <summary>
/// A message read by <see cref="IProtobufTranscoder.ToJson"/>: its canonical proto3 JSON (indented),
/// or why the bytes aren't that message — truncated, a field of the wrong wire type, a string that
/// isn't UTF-8. <see cref="Warnings"/> lists what was read but not shown, such as fields the .proto
/// doesn't declare (how protobuf stays forward compatible, so not an error).
/// </summary>
public sealed record ProtobufDecoding(string? Json, string? Error, IReadOnlyList<string> Warnings)
{
    public static ProtobufDecoding Failed(string error) => new(null, error, []);
}
