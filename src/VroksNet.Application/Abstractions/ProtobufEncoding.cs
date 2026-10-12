namespace VroksNet.Application.Abstractions;

/// <summary>A message written by <see cref="IProtobufTranscoder.ToBinary"/>: its bytes, or why the JSON couldn't be written as one (naming where, e.g. "lines[0].quantity: …").</summary>
public sealed record ProtobufEncoding(byte[]? Bytes, string? Error)
{
    public static ProtobufEncoding Failed(string error) => new(null, error);
}
